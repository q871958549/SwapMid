const DEFAULT_GITHUB_MODEL = 'openai/gpt-4.1-mini'
const GITHUB_MODELS_URL = 'https://models.github.ai/inference/chat/completions'
const GITHUB_API_VERSION = '2026-03-10'
const DEEPL_FREE_URL = 'https://api-free.deepl.com/v2/translate'
const DEFAULT_BATCH_SIZE = 40
const DEFAULT_BATCH_CHARACTERS = 5_000
const MAX_ATTEMPTS = 3
const MAX_PROTECTED_TERMS = 128
const MAX_ENGLISH_TERMS = 256
const MAX_TERM_CHARACTERS = 128
const MAX_MASKED_SOURCE_CHARACTERS = 512_000
const MAX_TRANSLATED_CHARACTERS = 1_000_000
const TERM_MARKER = '__SWAPMIDI18N_TERM_'

function translatorError(message, { retryable = false, fallbackToDeepL = false, retryAfterMs } = {}) {
  const error = new Error(message)
  error.retryable = retryable
  error.fallbackToDeepL = fallbackToDeepL
  error.retryAfterMs = retryAfterMs
  return error
}

function nonRetryable(message) {
  return translatorError(message)
}

function splitBatches(entries) {
  const batches = []
  let current = []
  let characters = 0

  for (const entry of entries) {
    const size = entry.key.length + entry.source.length
    if (
      current.length > 0 &&
      (current.length >= DEFAULT_BATCH_SIZE || characters + size > DEFAULT_BATCH_CHARACTERS)
    ) {
      batches.push(current)
      current = []
      characters = 0
    }
    current.push(entry)
    characters += size
  }

  if (current.length > 0) batches.push(current)
  return batches
}

function translationSchema(keys) {
  return {
    type: 'object',
    properties: {
      translations: {
        type: 'array',
        minItems: keys.length,
        maxItems: keys.length,
        items: {
          type: 'object',
          properties: {
            key: { type: 'string', enum: keys },
            value: { type: 'string' },
          },
          required: ['key', 'value'],
          additionalProperties: false,
        },
      },
    },
    required: ['translations'],
    additionalProperties: false,
  }
}

function instructions(config) {
  const protectedTerms = config.protectedTerms.join(', ')
  const glossary = Object.entries(config.englishTerms)
    .map(([source, english]) => `${source} => ${english}`)
    .join('\n')

  return [
    'Translate Simplified Chinese UI messages for an enterprise integration platform into concise, natural English.',
    'Return every input key exactly once and never change a key.',
    'Preserve all ICU arguments, plural/select syntax, punctuation placeholders, HTML-like rich-text tags, URLs, and code.',
    `Keep these product or technical terms unchanged when present: ${protectedTerms || '(none)'}.`,
    'Apply this glossary when the source contains an entry:',
    glossary || '(none)',
    'Translate only the user-visible wording. Do not add explanations.',
  ].join('\n')
}

function chatCompletionText(response) {
  const content = response?.choices?.[0]?.message?.content
  if (typeof content === 'string') return content
  if (Array.isArray(content)) {
    const text = content
      .filter((item) => item?.type === 'text' && typeof item.text === 'string')
      .map((item) => item.text)
      .join('')
    if (text) return text
  }
  throw nonRetryable('GitHub Models 未返回 choices[0].message.content')
}

function parseTranslations(text, batch, provider) {
  let payload
  try {
    payload = JSON.parse(text)
  } catch {
    throw nonRetryable(`${provider} 未返回合法 JSON`)
  }

  const translations = new Map()
  for (const item of payload.translations ?? []) {
    if (typeof item?.key !== 'string' || typeof item?.value !== 'string') {
      throw nonRetryable(`${provider} 返回了无效翻译项`)
    }
    if (translations.has(item.key)) throw nonRetryable(`${provider} 返回重复键：${item.key}`)
    translations.set(item.key, item.value)
  }

  const expected = new Set(batch.map(({ key }) => key))
  const missing = [...expected].filter((key) => !translations.has(key))
  const unexpected = [...translations.keys()].filter((key) => !expected.has(key))
  if (missing.length > 0 || unexpected.length > 0) {
    throw nonRetryable(
      `${provider} 键集合不一致；缺少 ${missing.join(', ') || '无'}；多出 ${unexpected.join(', ') || '无'}`,
    )
  }
  return translations
}

function retryAfterMs(response) {
  const raw = response.headers.get('retry-after')
  if (!raw) return undefined
  const seconds = Number(raw)
  return Number.isFinite(seconds) && seconds >= 0 ? Math.min(seconds * 1000, 30_000) : undefined
}

function githubErrorCategory(status) {
  if (status === 401 || status === 403) return 'access-denied'
  if (status === 429) return 'rate-limit'
  if (status >= 500) return 'server-error'
  return 'request-rejected'
}

async function requestGitHubBatch(batch, config, options) {
  const model =
    options.githubModel ?? process.env.I18N_GITHUB_MODEL ?? DEFAULT_GITHUB_MODEL
  const token = options.githubToken ?? process.env.GITHUB_TOKEN
  const fetchImplementation = options.fetchImplementation ?? fetch
  if (!token) {
    throw nonRetryable(
      `有 ${batch.length} 条英文文案需要生成，但未设置 GITHUB_TOKEN。` +
        'GitHub Actions 会自动提供；本地运行可把具有 models:read 权限的令牌注入该环境变量。',
    )
  }

  const response = await fetchImplementation(GITHUB_MODELS_URL, {
    method: 'POST',
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      'X-GitHub-Api-Version': GITHUB_API_VERSION,
    },
    body: JSON.stringify({
      model,
      messages: [
        { role: 'system', content: instructions(config) },
        { role: 'user', content: JSON.stringify({ messages: batch }) },
      ],
      temperature: 0,
      response_format: {
        type: 'json_schema',
        json_schema: {
          name: 'i18n_translations',
          strict: true,
          schema: translationSchema(batch.map(({ key }) => key)),
        },
      },
    }),
  })

  if (!response.ok) {
    const requestId =
      response.headers.get('x-github-request-id') ?? response.headers.get('x-request-id')
    const rateLimited = response.status === 429
    throw translatorError(
      `GitHub Models 调用失败（HTTP ${response.status}，category ${githubErrorCategory(response.status)}` +
        `${requestId ? `，request ${requestId}` : ''}）`,
      {
        fallbackToDeepL: rateLimited,
        retryable: rateLimited || response.status >= 500,
        retryAfterMs: retryAfterMs(response),
      },
    )
  }

  return parseTranslations(
    chatCompletionText(await response.json()),
    batch,
    'GitHub Models',
  )
}

export function maskDeepLTerms(source, config) {
  const protectedTerms = Array.isArray(config?.protectedTerms) ? config.protectedTerms : []
  const englishTerms = config?.englishTerms && typeof config.englishTerms === 'object'
    && !Array.isArray(config.englishTerms)
    ? Object.entries(config.englishTerms)
    : []
  if (protectedTerms.length > MAX_PROTECTED_TERMS || englishTerms.length > MAX_ENGLISH_TERMS) {
    throw nonRetryable('DeepL 术语数量超过安全上限')
  }
  if (source.includes(TERM_MARKER)) throw nonRetryable('源文使用了保留术语占位符前缀')

  const candidatesByTerm = new Map()
  for (const [term, english] of [
    ...englishTerms,
    ...protectedTerms.map(term => [term, term]),
  ]) {
    if (
      typeof term !== 'string'
      || typeof english !== 'string'
      || term.length < 2
      || term.length > MAX_TERM_CHARACTERS
      || english.length === 0
      || term.includes(TERM_MARKER)
      || english.includes(TERM_MARKER)
    ) {
      throw nonRetryable('DeepL 术语必须是有界、非空且不占用保留前缀的字符串')
    }
    if (candidatesByTerm.has(term)) throw nonRetryable(`DeepL 术语重复或冲突：${term}`)
    candidatesByTerm.set(term, english)
  }
  const candidates = [...candidatesByTerm]
    .filter(([term]) => source.includes(term))
    .sort(([left], [right]) => right.length - left.length || (left < right ? -1 : left > right ? 1 : 0))

  let text = ''
  const replacements = []
  const replacementsByTerm = new Map()
  for (let index = 0; index < source.length;) {
    const match = candidates.find(([term]) => source.startsWith(term, index))
    if (!match) {
      text += source[index]
      index += 1
    } else {
      let replacement = replacementsByTerm.get(match[0])
      if (!replacement) {
        replacement = {
          placeholder: `${TERM_MARKER}${replacements.length}__`,
          english: match[1],
          occurrences: 0,
        }
        replacementsByTerm.set(match[0], replacement)
        replacements.push(replacement)
      }
      replacement.occurrences += 1
      text += replacement.placeholder
      index += match[0].length
    }
    if (text.length > MAX_MASKED_SOURCE_CHARACTERS) {
      throw nonRetryable(`DeepL 术语遮罩结果超过 ${MAX_MASKED_SOURCE_CHARACTERS} 字符上限`)
    }
  }

  function countOccurrences(value, needle) {
    let count = 0
    let from = 0
    while (true) {
      const index = value.indexOf(needle, from)
      if (index < 0) return count
      count += 1
      from = index + needle.length
    }
  }

  return {
    text,
    restore(translated) {
      if (typeof translated !== 'string' || translated.length > MAX_TRANSLATED_CHARACTERS) {
        throw nonRetryable(`DeepL 返回文本超过 ${MAX_TRANSLATED_CHARACTERS} 字符上限`)
      }
      let restored = translated
      for (const { placeholder, english, occurrences } of replacements) {
        const actualOccurrences = countOccurrences(restored, placeholder)
        if (actualOccurrences !== occurrences) {
          throw nonRetryable(
            `DeepL 未原样保留术语占位符 ${placeholder}：期望 ${occurrences}，实际 ${actualOccurrences}`,
          )
        }
        restored = restored.replaceAll(placeholder, english)
      }
      if (/__SWAPMIDI18N_TERM_\d+__/.test(restored)) {
        throw nonRetryable('DeepL 返回了无法还原的术语占位符')
      }
      return restored
    },
  }
}

async function requestDeepLBatch(batch, config, options) {
  const apiKey = options.deepLApiKey ?? process.env.DEEPL_API_KEY
  const fetchImplementation = options.fetchImplementation ?? fetch
  if (!apiKey) {
    throw nonRetryable(
      `有 ${batch.length} 条英文文案需要走 DeepL 生成，但未设置 DEEPL_API_KEY。` +
        '请在 GitHub Actions Repository secrets 或本地环境配置 DeepL API Free 密钥。',
    )
  }

  const masked = batch.map(({ source }) => maskDeepLTerms(source, config))
  const response = await fetchImplementation(DEEPL_FREE_URL, {
    method: 'POST',
    headers: {
      Authorization: `DeepL-Auth-Key ${apiKey}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      text: masked.map(({ text }) => text),
      // DeepL 的 source_lang 中文只接受 'ZH'（ZH-HANS/ZH-HANT 细分仅用于 target_lang）；
      // 传 'ZH-HANS' 会 400「Value for 'source_lang' not supported」。此前英文总走 GitHub Models、
      // DeepL 回退从未真实触发，该 bug 长期潜伏，强制走 DeepL 后首次暴露（实测最小复现坐实）。
      source_lang: 'ZH',
      target_lang: 'EN-US',
      preserve_formatting: true,
      context:
        'Concise enterprise integration platform UI. Preserve ICU placeholders, markup tags, URLs, and code.',
    }),
  })

  if (!response.ok) {
    const quotaMessage =
      response.status === 456 ? '；DeepL API Free 本期字符额度已用完' : ''
    throw translatorError(
      `DeepL API Free 调用失败（HTTP ${response.status}）${quotaMessage}`,
      {
        retryable: response.status === 429 || response.status >= 500,
        retryAfterMs: retryAfterMs(response),
      },
    )
  }

  const payload = await response.json()
  if (!Array.isArray(payload.translations) || payload.translations.length !== batch.length) {
    throw nonRetryable(
      `DeepL API Free 返回数量不一致；期望 ${batch.length}，实际 ${payload.translations?.length ?? 0}`,
    )
  }

  return new Map(
    batch.map(({ key }, index) => {
      const translated = payload.translations[index]?.text
      if (typeof translated !== 'string') {
        throw nonRetryable(`DeepL API Free 返回了无效翻译项：${key}`)
      }
      return [key, masked[index].restore(translated)]
    }),
  )
}

async function withRetry(operation, options) {
  const sleepImplementation =
    options.sleepImplementation ?? ((milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)))
  let lastError
  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt += 1) {
    try {
      return await operation()
    } catch (error) {
      lastError = error
      if (error.retryable === false || attempt === MAX_ATTEMPTS) {
        throw error
      }
      await sleepImplementation(error.retryAfterMs ?? attempt * 1000)
    }
  }
  throw lastError
}

export async function translateEnglish(entries, config, options = {}) {
  const result = new Map()
  const batches = splitBatches(entries)
  // 起步通道可强制为 DeepL（options.englishProvider 或 I18N_ENGLISH_PROVIDER=deepl）：
  // 自托管 CI 用它绕开 GitHub Models——账户付款失败时 Models 可能返回 403（access-denied），
  // 而 403 不触发 DeepL 回退（见 requestGitHubBatch），会直接失败；DeepL 的可用性独立于 GitHub 账户计费。
  const forcedProvider = options.englishProvider ?? process.env.I18N_ENGLISH_PROVIDER
  let provider = forcedProvider === 'deepl' ? 'deepl' : 'github-models'

  for (const [index, batch] of batches.entries()) {
    options.onProgress?.({
      current: index + 1,
      total: batches.length,
      count: batch.length,
      provider,
    })

    let translated
    if (provider === 'github-models') {
      try {
        translated = await withRetry(
          () => requestGitHubBatch(batch, config, options),
          options,
        )
      } catch (error) {
        if (!error.fallbackToDeepL) throw error
        provider = 'deepl'
        const fallback = {
          from: 'github-models',
          to: 'deepl',
          reason: error.message,
        }
        if (options.onFallback) {
          options.onFallback(fallback)
        } else {
          console.warn(
            '[i18n] GitHub Models 触发免费额度或速率限制，本次任务后续改用 DeepL API Free。',
          )
        }
      }
    }

    if (!translated) {
      translated = await withRetry(
        () => requestDeepLBatch(batch, config, options),
        options,
      )
    }
    for (const [key, value] of translated) result.set(key, value)
  }

  return result
}
