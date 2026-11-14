import assert from 'node:assert/strict'
import test from 'node:test'

import { maskDeepLTerms, translateEnglish } from './i18n-english-translator.mjs'

// 隔离外部 I18N_ENGLISH_PROVIDER：本文件多数用例依赖默认 GitHub Models 起步，
// 若开发者环境把它设成 'deepl'（如本地手动补英文时），会污染这些用例——顶部清除保证隔离。
delete process.env.I18N_ENGLISH_PROVIDER

const config = {
  protectedTerms: ['API'],
  englishTerms: { 消息总线: 'MessageBus', 保存: 'Save' },
}

test('DeepL 术语遮罩拒绝空项并只对原始源文做一次最长匹配', () => {
  assert.throws(
    () => maskDeepLTerms('保存', { protectedTerms: ['', ''], englishTerms: {} }),
    /有界、非空/,
  )

  const masked = maskDeepLTerms('消息总线与消息', {
    protectedTerms: [],
    englishTerms: { 消息总线: 'MessageBus', 消息: 'Message' },
  })
  assert.equal(masked.text, '__SWAPMIDI18N_TERM_0__与__SWAPMIDI18N_TERM_1__')
  assert.equal(masked.restore(masked.text), 'MessageBus与Message')
  assert.throws(
    () => masked.restore(`${masked.text}__SWAPMIDI18N_TERM_0__`),
    /期望 1，实际 2/,
  )
})

test('DeepL 术语遮罩对恶意高密度匹配结果设置硬上限', () => {
  assert.throws(
    () => maskDeepLTerms('aa'.repeat(50_000), {
      protectedTerms: ['aa'],
      englishTerms: {},
    }),
    /遮罩结果超过 512000 字符上限/,
  )
})

function jsonResponse(payload, { status = 200, headers = {} } = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(headers),
    async json() {
      return payload
    },
  }
}

test('使用 GitHub Models 结构化输出并按 key 返回英文', async () => {
  let capturedUrl
  let capturedRequest
  let capturedHeaders
  const fetchImplementation = async (url, request) => {
    capturedUrl = url
    capturedRequest = JSON.parse(request.body)
    capturedHeaders = request.headers
    return jsonResponse({
      choices: [
        {
          message: {
            content: JSON.stringify({
              translations: [
                { key: 'Common.save', value: 'Save' },
                { key: 'Common.api', value: 'API URL' },
              ],
            }),
          },
        },
      ],
    })
  }

  const translated = await translateEnglish(
    [
      { key: 'Common.save', source: '保存' },
      { key: 'Common.api', source: 'API 地址' },
    ],
    config,
    {
      githubToken: 'test-token',
      githubModel: 'openai/test-model',
      fetchImplementation,
    },
  )

  assert.equal(capturedUrl, 'https://models.github.ai/inference/chat/completions')
  assert.equal(capturedHeaders.Authorization, 'Bearer test-token')
  assert.equal(capturedHeaders['X-GitHub-Api-Version'], '2026-03-10')
  assert.equal(capturedRequest.model, 'openai/test-model')
  assert.equal(capturedRequest.response_format.type, 'json_schema')
  assert.equal(capturedRequest.response_format.json_schema.strict, true)
  assert.equal(translated.get('Common.save'), 'Save')
  assert.equal(translated.get('Common.api'), 'API URL')
})

test('无 GitHub 令牌且存在待翻译项时给出可执行提示', async () => {
  await assert.rejects(
    () =>
      translateEnglish([{ key: 'Common.save', source: '保存' }], config, {
        githubToken: '',
        fetchImplementation: async () => {
          throw new Error('不应调用')
        },
      }),
    /GITHUB_TOKEN/,
  )
})

test('GitHub Models 限额后切换 DeepL 并保留术语', async () => {
  const calls = []
  let fallback
  const fetchImplementation = async (url, request) => {
    calls.push({ url, request })
    if (url.includes('models.github.ai')) {
      return jsonResponse(
        { message: 'free usage quota exceeded' },
        { status: 429, headers: { 'x-ratelimit-remaining': '0' } },
      )
    }

    const body = JSON.parse(request.body)
    assert.match(body.text[0], /__SWAPMIDI18N_TERM_0__/)
    assert.match(body.text[0], /__SWAPMIDI18N_TERM_1__/)
    return jsonResponse({
      translations: [{ text: `Manage ${body.text[0]}` }],
    })
  }

  const translated = await translateEnglish(
    [{ key: 'Common.busApi', source: '消息总线 API' }],
    config,
    {
      githubToken: 'github-token',
      deepLApiKey: 'deepl-key',
      fetchImplementation,
      sleepImplementation: async () => {},
      onFallback: (event) => {
        fallback = event
      },
    },
  )

  assert.equal(calls.length, 4)
  assert.equal(calls[3].url, 'https://api-free.deepl.com/v2/translate')
  assert.equal(calls[3].request.headers.Authorization, 'DeepL-Auth-Key deepl-key')
  assert.equal(fallback.from, 'github-models')
  assert.equal(fallback.to, 'deepl')
  assert.equal(translated.get('Common.busApi'), 'Manage MessageBus API')
})

test('GitHub Models 限额后剩余批次不再重复请求主通道', async () => {
  const calls = []
  const entries = Array.from({ length: 41 }, (_, index) => ({
    key: `Common.message${index}`,
    source: `文案 ${index}`,
  }))
  const fetchImplementation = async (url, request) => {
    calls.push(url)
    if (url.includes('models.github.ai')) {
      return jsonResponse({ message: 'free usage quota exceeded' }, { status: 429 })
    }
    const body = JSON.parse(request.body)
    return jsonResponse({
      translations: body.text.map((text) => ({ text: `English ${text}` })),
    })
  }

  const translated = await translateEnglish(entries, config, {
    githubToken: 'github-token',
    deepLApiKey: 'deepl-key',
    fetchImplementation,
    sleepImplementation: async () => {},
    onFallback() {},
  })

  assert.equal(calls.filter((url) => url.includes('models.github.ai')).length, 3)
  assert.equal(calls.filter((url) => url.includes('api-free.deepl.com')).length, 2)
  assert.equal(translated.size, entries.length)
})

test('GitHub Models 普通权限错误不允许 DeepL 掩盖', async () => {
  let calls = 0
  const fetchImplementation = async () => {
    calls += 1
    return jsonResponse(
      { message: 'Resource not accessible by integration' },
      { status: 403, headers: { 'x-ratelimit-remaining': '99' } },
    )
  }

  await assert.rejects(
    () =>
      translateEnglish([{ key: 'Common.save', source: '保存' }], config, {
        githubToken: 'github-token',
        deepLApiKey: 'deepl-key',
        fetchImplementation,
      }),
    /category access-denied/,
  )
  assert.equal(calls, 1)
})

test('GitHub Models 限额后缺少 DeepL 密钥时明确失败', async () => {
  await assert.rejects(
    () =>
      translateEnglish([{ key: 'Common.save', source: '保存' }], config, {
        githubToken: 'github-token',
        deepLApiKey: '',
        fetchImplementation: async () =>
          jsonResponse({ message: 'rate limit exceeded' }, { status: 429 }),
        sleepImplementation: async () => {},
        onFallback() {},
      }),
    /DEEPL_API_KEY/,
  )
})

test('GitHub Models 服务端错误会重试', async () => {
  let calls = 0
  const fetchImplementation = async () => {
    calls += 1
    if (calls === 1) {
      return jsonResponse({ message: 'temporary outage' }, { status: 503 })
    }
    return jsonResponse({
      choices: [
        {
          message: {
            content: JSON.stringify({
              translations: [{ key: 'Common.save', value: 'Save' }],
            }),
          },
        },
      ],
    })
  }

  const translated = await translateEnglish(
    [{ key: 'Common.save', source: '保存' }],
    config,
    {
      githubToken: 'github-token',
      fetchImplementation,
      sleepImplementation: async () => {},
    },
  )

  assert.equal(calls, 2)
  assert.equal(translated.get('Common.save'), 'Save')
})

test('GitHub Models 短期限流先重试而不是立即消耗 DeepL', async () => {
  let calls = 0
  const fetchImplementation = async (url) => {
    calls += 1
    assert.match(url, /models\.github\.ai/)
    if (calls === 1) {
      return jsonResponse(
        { message: 'rate limit exceeded' },
        { status: 429, headers: { 'retry-after': '1' } },
      )
    }
    return jsonResponse({
      choices: [
        {
          message: {
            content: JSON.stringify({
              translations: [{ key: 'Common.save', value: 'Save' }],
            }),
          },
        },
      ],
    })
  }

  const delays = []
  const translated = await translateEnglish(
    [{ key: 'Common.save', source: '保存' }],
    config,
    {
      githubToken: 'github-token',
      deepLApiKey: 'deepl-key',
      fetchImplementation,
      sleepImplementation: async (milliseconds) => delays.push(milliseconds),
    },
  )

  assert.equal(calls, 2)
  assert.deepEqual(delays, [1000])
  assert.equal(translated.get('Common.save'), 'Save')
})

test('拒绝缺键或重复键的 GitHub Models 响应', async () => {
  const fetchImplementation = async () =>
    jsonResponse({
      choices: [
        {
          message: {
            content: JSON.stringify({
              translations: [
                { key: 'Common.save', value: 'Save' },
                { key: 'Common.save', value: 'Persist' },
              ],
            }),
          },
        },
      ],
    })

  await assert.rejects(
    () =>
      translateEnglish(
        [
          { key: 'Common.save', source: '保存' },
          { key: 'Common.cancel', source: '取消' },
        ],
        config,
        { githubToken: 'github-token', fetchImplementation },
      ),
    /重复键/,
  )
})

test('DeepL 免费字符额度耗尽时明确失败', async () => {
  let calls = 0
  const fetchImplementation = async (url) => {
    calls += 1
    if (url.includes('models.github.ai')) {
      return jsonResponse({ message: 'quota exceeded' }, { status: 429 })
    }
    return jsonResponse({ message: 'Quota exceeded' }, { status: 456 })
  }

  await assert.rejects(
    () =>
      translateEnglish([{ key: 'Common.save', source: '保存' }], config, {
        githubToken: 'github-token',
        deepLApiKey: 'deepl-key',
        fetchImplementation,
        sleepImplementation: async () => {},
        onFallback() {},
      }),
    /DeepL API Free 本期字符额度已用完/,
  )
  assert.equal(calls, 4)
})

test('englishProvider=deepl 起步即走 DeepL，完全不触碰 GitHub Models', async () => {
  const calls = []
  let deeplBody
  const fetchImplementation = async (url, request) => {
    calls.push(url)
    const body = JSON.parse(request.body)
    deeplBody = body
    return jsonResponse({
      translations: body.text.map((text) => ({ text: `EN:${text}` })),
    })
  }

  const translated = await translateEnglish(
    [{ key: 'Common.greeting', source: '你好' }],
    config,
    {
      englishProvider: 'deepl',
      deepLApiKey: 'deepl-key',
      fetchImplementation,
    },
  )

  assert.equal(calls.length, 1)
  assert.equal(calls[0], 'https://api-free.deepl.com/v2/translate')
  // DeepL 的 source_lang 只接受 'ZH'（传 ZH-HANS 会 400），焊死防回归
  assert.equal(deeplBody.source_lang, 'ZH')
  assert.equal(deeplBody.target_lang, 'EN-US')
  assert.equal(translated.get('Common.greeting'), 'EN:你好')
})

test('I18N_ENGLISH_PROVIDER=deepl 环境变量同样强制 DeepL 起步', async () => {
  const previous = process.env.I18N_ENGLISH_PROVIDER
  process.env.I18N_ENGLISH_PROVIDER = 'deepl'
  try {
    const calls = []
    const fetchImplementation = async (url, request) => {
      calls.push(url)
      const body = JSON.parse(request.body)
      return jsonResponse({
        translations: body.text.map((text) => ({ text: `EN:${text}` })),
      })
    }

    const translated = await translateEnglish(
      [{ key: 'Common.greeting', source: '你好' }],
      config,
      { deepLApiKey: 'deepl-key', fetchImplementation },
    )

    assert.equal(calls.filter((url) => url.includes('models.github.ai')).length, 0)
    assert.equal(translated.get('Common.greeting'), 'EN:你好')
  } finally {
    if (previous === undefined) delete process.env.I18N_ENGLISH_PROVIDER
    else process.env.I18N_ENGLISH_PROVIDER = previous
  }
})

test('englishProvider=deepl 但缺 DEEPL_API_KEY 时直接失败且不回退 GitHub Models', async () => {
  await assert.rejects(
    () =>
      translateEnglish([{ key: 'Common.greeting', source: '你好' }], config, {
        englishProvider: 'deepl',
        deepLApiKey: '',
        fetchImplementation: async () => {
          throw new Error('不应发起任何请求')
        },
      }),
    /DEEPL_API_KEY/,
  )
})
