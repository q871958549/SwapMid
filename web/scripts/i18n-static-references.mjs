import ts from 'typescript'

function isScopeNode(node) {
  return ts.isSourceFile(node)
    || ts.isBlock(node)
    || ts.isModuleBlock(node)
    || ts.isFunctionLike(node)
    || ts.isCatchClause(node)
    || node.kind === ts.SyntaxKind.CaseBlock
}

function unwrapExpression(expression) {
  let current = expression
  while (current) {
    if (ts.isParenthesizedExpression(current)
      || ts.isAwaitExpression(current)
      || ts.isAsExpression(current)
      || ts.isTypeAssertionExpression(current)
      || ts.isNonNullExpression(current)
      || ts.isSatisfiesExpression(current)) {
      current = current.expression
      continue
    }
    break
  }
  return current
}

function staticString(node) {
  const expression = unwrapExpression(node)
  return expression && ts.isStringLiteralLike(expression) ? expression.text : null
}

function propertyNameText(name) {
  return ts.isIdentifier(name) || ts.isStringLiteralLike(name) || ts.isNumericLiteral(name)
    ? name.text
    : null
}

const MAX_STATIC_PROPERTY_OCCURRENCES = 4_096
const MAX_STATIC_PROPERTY_VALUES = 256
const MAX_STATIC_PROPERTY_DIAGNOSTIC_LINES = 200

function staticPropertyLimitError(fileName, detail) {
  const error = new Error(
    `[i18n-static] ${fileName} 固定动态键来源超过资源上限（${detail}）；`
      + '为避免固定导航属性在审计前物化超大集合，已拒绝继续分析。',
  )
  error.name = 'I18nStaticPropertyLimitError'
  error.code = 'I18N_STATIC_PROPERTY_LIMIT'
  return error
}

/**
 * 只供受信 checker 的固定源码契约使用。调用方决定固定文件与固定属性名，
 * PR 注册表不能指定任意读取路径；非静态值必须显式报错，不能静默缩小候选键闭集。
 */
export function inspectStaticPropertyValuesInSource(source, propertyName, fileName = 'source.tsx') {
  const sourceFile = ts.createSourceFile(
    fileName,
    source,
    ts.ScriptTarget.Latest,
    true,
    fileName.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
  )
  const values = new Set()
  const nonStaticLines = []
  let occurrences = 0
  let nonStaticCount = 0

  function visit(node) {
    if (ts.isPropertyAssignment(node) && propertyNameText(node.name) === propertyName) {
      occurrences += 1
      if (occurrences > MAX_STATIC_PROPERTY_OCCURRENCES) {
        throw staticPropertyLimitError(
          fileName,
          `${propertyName} 命中数不得超过 ${MAX_STATIC_PROPERTY_OCCURRENCES}`,
        )
      }
      const value = staticString(node.initializer)
      if (value === null) {
        nonStaticCount += 1
        if (nonStaticLines.length < MAX_STATIC_PROPERTY_DIAGNOSTIC_LINES) {
          nonStaticLines.push(sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line + 1)
        }
      } else {
        if (!values.has(value) && values.size >= MAX_STATIC_PROPERTY_VALUES) {
          throw staticPropertyLimitError(
            fileName,
            `${propertyName} 唯一静态值不得超过 ${MAX_STATIC_PROPERTY_VALUES}`,
          )
        }
        values.add(value)
      }
    } else if (ts.isShorthandPropertyAssignment(node) && node.name.text === propertyName) {
      occurrences += 1
      if (occurrences > MAX_STATIC_PROPERTY_OCCURRENCES) {
        throw staticPropertyLimitError(
          fileName,
          `${propertyName} 命中数不得超过 ${MAX_STATIC_PROPERTY_OCCURRENCES}`,
        )
      }
      nonStaticCount += 1
      if (nonStaticLines.length < MAX_STATIC_PROPERTY_DIAGNOSTIC_LINES) {
        nonStaticLines.push(sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line + 1)
      }
    }
    ts.forEachChild(node, visit)
  }

  visit(sourceFile)
  return Object.freeze({
    values: Object.freeze([...values].sort()),
    nonStaticLines: Object.freeze(nonStaticLines),
    nonStaticCount,
    occurrences,
  })
}

function translatorBinding(initializer, sourceFile) {
  const expression = unwrapExpression(initializer)
  if (!expression || !ts.isCallExpression(expression) || !ts.isIdentifier(expression.expression)) return null
  if (expression.expression.text !== 'useTranslations' && expression.expression.text !== 'getTranslations') return null

  if (expression.arguments.length === 0) {
    return { kind: 'translator', namespace: '', namespaceExpression: '' }
  }

  const directNamespace = staticString(expression.arguments[0])
  if (directNamespace !== null) {
    return { kind: 'translator', namespace: directNamespace, namespaceExpression: directNamespace }
  }

  const options = unwrapExpression(expression.arguments[0])
  if (options && ts.isObjectLiteralExpression(options)) {
    for (const property of options.properties) {
      if (!ts.isPropertyAssignment(property)) continue
      if (propertyNameText(property.name) !== 'namespace') continue
      const namespace = staticString(property.initializer)
      return {
        kind: 'translator',
        namespace,
        namespaceExpression: property.initializer.getText(sourceFile),
      }
    }
    return { kind: 'translator', namespace: '', namespaceExpression: '' }
  }

  return {
    kind: 'translator',
    namespace: null,
    namespaceExpression: expression.arguments[0].getText(sourceFile),
  }
}

function registerBindingName(name, scope, binding) {
  if (ts.isIdentifier(name)) {
    scope.bindings.set(name.text, binding)
    return
  }
  for (const element of name.elements) {
    if (!ts.isOmittedExpression(element)) registerBindingName(element.name, scope, null)
  }
}

function registerValueName(name, scope, descriptor) {
  if (ts.isIdentifier(name)) {
    scope.values.set(name.text, descriptor)
    return
  }
  for (const element of name.elements) {
    if (!ts.isOmittedExpression(element)) registerValueName(element.name, scope, null)
  }
}

function registerDeclarationName(node, scope) {
  if (!scope || !node.name || !ts.isIdentifier(node.name)) return
  if (ts.isFunctionDeclaration(node)
    || ts.isClassDeclaration(node)
    || ts.isEnumDeclaration(node)) {
    scope.bindings.set(node.name.text, null)
    scope.values.set(node.name.text, null)
  }
}

function isConstVariableDeclaration(node) {
  return ts.isVariableDeclarationList(node.parent)
    && (node.parent.flags & ts.NodeFlags.Const) !== 0
}

function inferStateTypeDescriptor(node, scope) {
  const initializer = unwrapExpression(node.initializer)
  if (!initializer
    || !ts.isCallExpression(initializer)
    || !ts.isIdentifier(initializer.expression)
    || initializer.expression.text !== 'useState'
    || !initializer.typeArguments?.[0]) return null

  return { kind: 'type', node: initializer.typeArguments[0], scope }
}

function callbackIterableDescriptor(parameter, scope) {
  if (!ts.isIdentifier(parameter.name)) return null
  const fn = parameter.parent
  if (!ts.isFunctionExpression(fn) && !ts.isArrowFunction(fn)) return null
  if (fn.parameters[0] !== parameter) return null
  const call = fn.parent
  if (!ts.isCallExpression(call) || !call.arguments.includes(fn)) return null
  const callee = unwrapExpression(call.expression)
  if (!callee || !ts.isPropertyAccessExpression(callee)) return null
  if (!['map', 'flatMap', 'filter', 'find', 'some', 'every'].includes(callee.name.text)) return null
  return { kind: 'iterable-element', node: callee.expression, scope: scope.parent }
}

function buildScopes(sourceFile) {
  const scopeByNode = new WeakMap()

  function visit(node, parentScope) {
    registerDeclarationName(node, parentScope)
    let scope = parentScope
    if (isScopeNode(node)) {
      scope = { parent: parentScope, bindings: new Map(), values: new Map(), types: new Map() }
      scopeByNode.set(node, scope)
      if (ts.isFunctionExpression(node) && node.name) {
        scope.bindings.set(node.name.text, null)
        scope.values.set(node.name.text, null)
      }
    }

    if (ts.isTypeAliasDeclaration(node)) {
      scope.types.set(node.name.text, node.type)
    } else if (ts.isVariableDeclaration(node)) {
      registerBindingName(node.name, scope, translatorBinding(node.initializer, sourceFile))
      const stateType = ts.isArrayBindingPattern(node.name) ? inferStateTypeDescriptor(node, scope) : null
      if (stateType) {
        node.name.elements.forEach((element, index) => {
          if (!ts.isOmittedExpression(element)) {
            registerValueName(element.name, scope, index === 0 ? stateType : null)
          }
        })
      } else {
        registerValueName(
          node.name,
          scope,
          isConstVariableDeclaration(node) && node.initializer
            ? { kind: 'expression', node: node.initializer, scope }
            : null,
        )
      }
    } else if (ts.isParameter(node)) {
      registerBindingName(node.name, scope, null)
      registerValueName(node.name, scope, callbackIterableDescriptor(node, scope))
    } else if (ts.isImportClause(node) && node.name) {
      scope.bindings.set(node.name.text, null)
      scope.values.set(node.name.text, null)
    } else if (ts.isNamespaceImport(node)) {
      scope.bindings.set(node.name.text, null)
      scope.values.set(node.name.text, null)
    } else if (ts.isImportSpecifier(node)) {
      scope.bindings.set(node.name.text, null)
      scope.values.set(node.name.text, null)
    }

    ts.forEachChild(node, child => visit(child, scope))
  }

  visit(sourceFile, null)
  return scopeByNode
}

function translatorCallTarget(call) {
  const expression = call.expression
  if (ts.isIdentifier(expression)) return { variable: expression.text, method: 'plain' }
  if (!ts.isPropertyAccessExpression(expression) || !ts.isIdentifier(expression.expression)) return null
  if (!['rich', 'markup', 'raw'].includes(expression.name.text)) return null
  return { variable: expression.expression.text, method: expression.name.text }
}

function resolveScopedMap(scope, mapName, name) {
  for (let current = scope; current; current = current.parent) {
    if (current[mapName].has(name)) return { found: true, value: current[mapName].get(name) }
  }
  return { found: false, value: null }
}

function resolved(values = []) {
  return { resolved: true, values: values instanceof Set ? values : new Set(values) }
}

const UNRESOLVED = Object.freeze({ resolved: false, values: new Set() })

// 求值对象来自 PR 源文本，必须先证明规模有界再分配结果集合。上限按单文件重置，
// 既覆盖正常的有限动态键，又阻止一个恶意表达式耗尽 self-hosted runner。
const MAX_STATIC_CANDIDATES_PER_EXPRESSION = 2_048
const MAX_STATIC_EVALUATION_OPERATIONS_PER_FILE = 20_000
const MAX_STATIC_REFERENCES_PER_FILE = 4_096
const MAX_NAMESPACE_INDEX_OPERATIONS_PER_FILE = 100_000
const MAX_NAMESPACE_INDEX_ASSOCIATIONS_PER_FILE = 8_192

export const I18N_STATIC_BATCH_LIMITS = Object.freeze({
  operations: 2_000_000,
  references: 100_000,
  namespaceIndexOperations: 10_000_000,
  namespaceIndexAssociations: 100_000,
})

export function createI18nStaticBatchBudget(overrides = {}) {
  const limits = { ...I18N_STATIC_BATCH_LIMITS, ...overrides }
  for (const [name, value] of Object.entries(limits)) {
    if (!Number.isSafeInteger(value) || value <= 0) {
      throw new Error(`i18n 静态分析批次预算 ${name} 必须是正安全整数`)
    }
  }
  return {
    limits: Object.freeze(limits),
    operations: 0,
    references: 0,
    namespaceIndexOperations: 0,
    namespaceIndexAssociations: 0,
  }
}

function evaluationBudget(fileName, batch) {
  return {
    fileName,
    batch,
    operations: 0,
    references: 0,
    namespaceIndexOperations: 0,
    namespaceIndexAssociations: 0,
  }
}

function batchLimitError(budget, field, label, detail) {
  const limit = budget.batch.limits[field]
  const error = new Error(
    `[i18n-static] 全批次${label} ${limit} 已超过（${budget.fileName}: ${detail}）；` +
      '为避免大量小文件线性放大可信预检资源，已拒绝继续分析。',
  )
  error.name = 'I18nStaticBatchLimitError'
  error.code = 'I18N_STATIC_BATCH_LIMIT'
  return error
}

function assertBatchCapacity(budget, field, amount, label, detail) {
  if (!budget.batch) return
  const current = budget.batch[field]
  const limit = budget.batch.limits[field]
  if (!Number.isSafeInteger(amount) || amount < 0 || amount > limit - current) {
    throw batchLimitError(budget, field, label, detail)
  }
}

function commitBatchUsage(budget, field, amount) {
  if (budget.batch) budget.batch[field] += amount
}

function hardLimitError(budget, limitName, limit, detail) {
  const error = new Error(
    `[i18n-static] ${budget.fileName} ${limitName} ${limit} 已超过（${detail}）；` +
      '为避免静态求值组合爆炸，已拒绝继续物化。请缩小联合类型或拆分动态键表达式。',
  )
  error.name = 'I18nStaticEvaluationLimitError'
  error.code = 'I18N_STATIC_EVALUATION_LIMIT'
  return error
}

function reserveEvaluationOperations(budget, amount, detail) {
  if (!Number.isSafeInteger(amount)
    || amount < 0
    || amount > MAX_STATIC_EVALUATION_OPERATIONS_PER_FILE - budget.operations) {
    throw hardLimitError(
      budget,
      '单文件求值操作硬上限',
      MAX_STATIC_EVALUATION_OPERATIONS_PER_FILE,
      detail,
    )
  }
  assertBatchCapacity(budget, 'operations', amount, '求值操作硬上限', detail)
  budget.operations += amount
  commitBatchUsage(budget, 'operations', amount)
}

function reserveReferenceVolume(budget, amount, detail) {
  if (!Number.isSafeInteger(amount)
    || amount < 0
    || amount > MAX_STATIC_REFERENCES_PER_FILE - budget.references) {
    throw hardLimitError(
      budget,
      '单文件静态引用量硬上限',
      MAX_STATIC_REFERENCES_PER_FILE,
      detail,
    )
  }
  assertBatchCapacity(budget, 'references', amount, '静态引用量硬上限', detail)
  budget.references += amount
  commitBatchUsage(budget, 'references', amount)
}

function reserveNamespaceIndexOperations(budget, amount, detail) {
  if (!Number.isSafeInteger(amount)
    || amount < 0
    || amount > MAX_NAMESPACE_INDEX_OPERATIONS_PER_FILE - budget.namespaceIndexOperations) {
    throw hardLimitError(
      budget,
      '单文件 namespace 后缀索引操作硬上限',
      MAX_NAMESPACE_INDEX_OPERATIONS_PER_FILE,
      detail,
    )
  }
  assertBatchCapacity(
    budget,
    'namespaceIndexOperations',
    amount,
    'namespace 后缀索引操作硬上限',
    detail,
  )
  budget.namespaceIndexOperations += amount
  commitBatchUsage(budget, 'namespaceIndexOperations', amount)
}

function reserveNamespaceIndexAssociation(budget, detail) {
  if (budget.namespaceIndexAssociations >= MAX_NAMESPACE_INDEX_ASSOCIATIONS_PER_FILE) {
    throw hardLimitError(
      budget,
      '单文件 namespace 后缀索引关联量硬上限',
      MAX_NAMESPACE_INDEX_ASSOCIATIONS_PER_FILE,
      detail,
    )
  }
  assertBatchCapacity(
    budget,
    'namespaceIndexAssociations',
    1,
    'namespace 后缀索引关联量硬上限',
    detail,
  )
  budget.namespaceIndexAssociations += 1
  commitBatchUsage(budget, 'namespaceIndexAssociations', 1)
}

function assertCandidateCapacity(budget, count, detail) {
  if (!Number.isSafeInteger(count) || count > MAX_STATIC_CANDIDATES_PER_EXPRESSION) {
    throw hardLimitError(
      budget,
      '单表达式候选数硬上限',
      MAX_STATIC_CANDIDATES_PER_EXPRESSION,
      detail,
    )
  }
}

function combineResolved(results, budget, detail = '集合合并') {
  if (results.some(result => !result.resolved)) return UNRESOLVED
  let projectedSize = 0
  for (const result of results) {
    if (result.values.size > MAX_STATIC_CANDIDATES_PER_EXPRESSION - projectedSize) {
      throw hardLimitError(
        budget,
        '单表达式候选数硬上限',
        MAX_STATIC_CANDIDATES_PER_EXPRESSION,
        `${detail}至少需要 ${projectedSize + result.values.size} 个候选`,
      )
    }
    projectedSize += result.values.size
  }
  assertCandidateCapacity(budget, projectedSize, `${detail}预计 ${projectedSize} 个候选`)
  reserveEvaluationOperations(budget, projectedSize, `${detail}需合并 ${projectedSize} 个候选`)
  const values = new Set()
  for (const result of results) {
    for (const value of result.values) values.add(value)
  }
  return resolved(values)
}

function concatenateResolved(left, right, budget, detail = '字符串拼接') {
  if (!left.resolved || !right.resolved || left.values.size === 0 || right.values.size === 0) return UNRESOLVED
  if (left.values.size > Math.floor(MAX_STATIC_CANDIDATES_PER_EXPRESSION / right.values.size)) {
    throw hardLimitError(
      budget,
      '单表达式候选数硬上限',
      MAX_STATIC_CANDIDATES_PER_EXPRESSION,
      `${detail}笛卡尔积预计 ${left.values.size} × ${right.values.size} 个候选`,
    )
  }
  const projectedSize = left.values.size * right.values.size
  assertCandidateCapacity(budget, projectedSize, `${detail}预计 ${projectedSize} 个候选`)
  reserveEvaluationOperations(budget, projectedSize, `${detail}需拼接 ${projectedSize} 个候选`)
  const values = new Set()
  for (const leftValue of left.values) {
    for (const rightValue of right.values) values.add(leftValue + rightValue)
  }
  return resolved(values)
}

function buildCandidateNamespaceIndex(knownKeys, requestedKeys, budget) {
  const index = new Map([...requestedKeys].map(key => [key, new Set()]))
  if (requestedKeys.size === 0) return index

  function addMatch(key, namespace) {
    const matches = index.get(key)
    if (!matches || matches.has(namespace)) return
    reserveNamespaceIndexAssociation(
      budget,
      `相对键 ${key} 的候选 namespace 将超过 ${MAX_NAMESPACE_INDEX_ASSOCIATIONS_PER_FILE} 个关联`,
    )
    matches.add(namespace)
  }

  for (const fullKey of knownKeys) {
    reserveNamespaceIndexOperations(budget, 1, '扫描完整消息键')
    if (requestedKeys.has(fullKey)) addMatch(fullKey, '')

    for (let dot = fullKey.indexOf('.'); dot >= 0; dot = fullKey.indexOf('.', dot + 1)) {
      reserveNamespaceIndexOperations(budget, 1, '构建消息键后缀索引')
      const relativeKey = fullKey.slice(dot + 1)
      if (requestedKeys.has(relativeKey)) addMatch(relativeKey, fullKey.slice(0, dot))
    }
  }

  return index
}

function evaluateStringType(node, scope, seen, budget) {
  reserveEvaluationOperations(budget, 1, '解析字符串类型')
  if (!node) return UNRESOLVED
  if (ts.isParenthesizedTypeNode(node)) return evaluateStringType(node.type, scope, seen, budget)
  if (ts.isUnionTypeNode(node)) {
    return combineResolved(
      node.types.map(type => evaluateStringType(type, scope, seen, budget)),
      budget,
      '字符串联合类型集合合并',
    )
  }
  if (ts.isLiteralTypeNode(node)) {
    if (ts.isStringLiteralLike(node.literal)) return resolved([node.literal.text])
    if (node.literal.kind === ts.SyntaxKind.NullKeyword) return resolved()
    return UNRESOLVED
  }
  if (node.kind === ts.SyntaxKind.NullKeyword || node.kind === ts.SyntaxKind.UndefinedKeyword) return resolved()
  if (ts.isTypeReferenceNode(node) && ts.isIdentifier(node.typeName)) {
    const type = resolveScopedMap(scope, 'types', node.typeName.text)
    if (!type.found || !type.value || seen.has(type.value)) return UNRESOLVED
    const nextSeen = new Set(seen).add(type.value)
    return evaluateStringType(type.value, scope, nextSeen, budget)
  }
  return UNRESOLVED
}

function evaluateObjectProperty(node, propertyName, scope, seen, budget) {
  reserveEvaluationOperations(budget, 1, '解析对象属性')
  const expression = unwrapExpression(node)
  if (!expression) return UNRESOLVED

  if (ts.isIdentifier(expression)) {
    const binding = resolveScopedMap(scope, 'values', expression.text)
    if (!binding.found || !binding.value || seen.has(binding.value)) return UNRESOLVED
    const nextSeen = new Set(seen).add(binding.value)
    if (binding.value.kind === 'expression') {
      return evaluateObjectProperty(binding.value.node, propertyName, binding.value.scope, nextSeen, budget)
    }
    if (binding.value.kind === 'iterable-element') {
      return evaluateIterableProperty(binding.value.node, propertyName, binding.value.scope, nextSeen, budget)
    }
    return UNRESOLVED
  }

  if (ts.isObjectLiteralExpression(expression)) {
    const matches = []
    for (const property of expression.properties) {
      if (ts.isPropertyAssignment(property) && propertyNameText(property.name) === propertyName) {
        matches.push(evaluateStringExpression(property.initializer, scope, seen, budget))
      } else if (ts.isShorthandPropertyAssignment(property) && property.name.text === propertyName) {
        matches.push(evaluateStringExpression(property.name, scope, seen, budget))
      }
    }
    return matches.length > 0
      ? combineResolved(matches, budget, `对象属性 ${propertyName} 集合合并`)
      : UNRESOLVED
  }

  if (ts.isElementAccessExpression(expression)) {
    // Map 的索引来自运行时，但 map 本身是 const 对象时，可枚举所有 value 再读取其目标属性。
    return evaluateObjectMapValueProperty(expression.expression, propertyName, scope, seen, budget)
  }

  if (ts.isCallExpression(expression)) {
    const callee = unwrapExpression(expression.expression)
    if (callee && ts.isPropertyAccessExpression(callee) && ['find', 'filter'].includes(callee.name.text)) {
      return evaluateIterableProperty(callee.expression, propertyName, scope, seen, budget)
    }
  }

  if (ts.isConditionalExpression(expression)) {
    return combineResolved(
      [
        evaluateObjectProperty(expression.whenTrue, propertyName, scope, seen, budget),
        evaluateObjectProperty(expression.whenFalse, propertyName, scope, seen, budget),
      ],
      budget,
      `对象属性 ${propertyName} 条件分支集合合并`,
    )
  }

  return UNRESOLVED
}

function evaluateObjectMapValueProperty(node, propertyName, scope, seen, budget) {
  reserveEvaluationOperations(budget, 1, '解析对象 Map 属性')
  const expression = unwrapExpression(node)
  if (!expression) return UNRESOLVED

  if (ts.isIdentifier(expression)) {
    const binding = resolveScopedMap(scope, 'values', expression.text)
    if (!binding.found || !binding.value || binding.value.kind !== 'expression' || seen.has(binding.value)) {
      return UNRESOLVED
    }
    return evaluateObjectMapValueProperty(
      binding.value.node,
      propertyName,
      binding.value.scope,
      new Set(seen).add(binding.value),
      budget,
    )
  }

  if (ts.isObjectLiteralExpression(expression)) {
    const values = []
    for (const property of expression.properties) {
      if (!ts.isPropertyAssignment(property)) return UNRESOLVED
      values.push(evaluateObjectProperty(property.initializer, propertyName, scope, seen, budget))
    }
    return values.length > 0
      ? combineResolved(values, budget, `对象 Map 属性 ${propertyName} 集合合并`)
      : UNRESOLVED
  }

  return UNRESOLVED
}

function evaluateIterableProperty(node, propertyName, scope, seen, budget) {
  reserveEvaluationOperations(budget, 1, '解析可迭代对象属性')
  const expression = unwrapExpression(node)
  if (!expression) return UNRESOLVED

  if (ts.isIdentifier(expression)) {
    const binding = resolveScopedMap(scope, 'values', expression.text)
    if (!binding.found || !binding.value || binding.value.kind !== 'expression' || seen.has(binding.value)) {
      return UNRESOLVED
    }
    return evaluateIterableProperty(
      binding.value.node,
      propertyName,
      binding.value.scope,
      new Set(seen).add(binding.value),
      budget,
    )
  }

  if (ts.isArrayLiteralExpression(expression)) {
    const values = expression.elements.map(element => (
      evaluateObjectProperty(element, propertyName, scope, seen, budget)
    ))
    return values.length > 0
      ? combineResolved(values, budget, `可迭代属性 ${propertyName} 集合合并`)
      : UNRESOLVED
  }

  if (ts.isConditionalExpression(expression)) {
    return combineResolved(
      [
        evaluateIterableProperty(expression.whenTrue, propertyName, scope, seen, budget),
        evaluateIterableProperty(expression.whenFalse, propertyName, scope, seen, budget),
      ],
      budget,
      `可迭代属性 ${propertyName} 条件分支集合合并`,
    )
  }

  return UNRESOLVED
}

function evaluateIterableStrings(node, scope, seen, budget) {
  reserveEvaluationOperations(budget, 1, '解析可迭代字符串')
  const expression = unwrapExpression(node)
  if (!expression) return UNRESOLVED

  if (ts.isIdentifier(expression)) {
    const binding = resolveScopedMap(scope, 'values', expression.text)
    if (!binding.found || !binding.value || binding.value.kind !== 'expression' || seen.has(binding.value)) {
      return UNRESOLVED
    }
    return evaluateIterableStrings(
      binding.value.node,
      binding.value.scope,
      new Set(seen).add(binding.value),
      budget,
    )
  }

  if (ts.isArrayLiteralExpression(expression)) {
    const values = expression.elements.map(element => evaluateStringExpression(element, scope, seen, budget))
    return values.length > 0
      ? combineResolved(values, budget, '可迭代字符串集合合并')
      : UNRESOLVED
  }

  return UNRESOLVED
}

function evaluateAllObjectStringValues(node, scope, seen, budget) {
  reserveEvaluationOperations(budget, 1, '解析对象全部字符串值')
  const expression = unwrapExpression(node)
  if (!expression) return UNRESOLVED

  if (ts.isIdentifier(expression)) {
    const binding = resolveScopedMap(scope, 'values', expression.text)
    if (!binding.found || !binding.value || binding.value.kind !== 'expression' || seen.has(binding.value)) {
      return UNRESOLVED
    }
    return evaluateAllObjectStringValues(
      binding.value.node,
      binding.value.scope,
      new Set(seen).add(binding.value),
      budget,
    )
  }

  if (ts.isObjectLiteralExpression(expression)) {
    const values = []
    for (const property of expression.properties) {
      if (!ts.isPropertyAssignment(property)) return UNRESOLVED
      values.push(evaluateStringExpression(property.initializer, scope, seen, budget))
    }
    return values.length > 0
      ? combineResolved(values, budget, '对象全部字符串值集合合并')
      : UNRESOLVED
  }

  return UNRESOLVED
}

function evaluateStringExpression(node, scope, seen = new Set(), budget) {
  reserveEvaluationOperations(budget, 1, '解析字符串表达式')
  const expression = unwrapExpression(node)
  if (!expression) return UNRESOLVED
  if (ts.isStringLiteralLike(expression)) return resolved([expression.text])

  if (ts.isIdentifier(expression)) {
    const binding = resolveScopedMap(scope, 'values', expression.text)
    if (!binding.found || !binding.value || seen.has(binding.value)) return UNRESOLVED
    const nextSeen = new Set(seen).add(binding.value)
    if (binding.value.kind === 'expression') {
      return evaluateStringExpression(binding.value.node, binding.value.scope, nextSeen, budget)
    }
    if (binding.value.kind === 'type') {
      return evaluateStringType(binding.value.node, binding.value.scope, nextSeen, budget)
    }
    if (binding.value.kind === 'iterable-element') {
      return evaluateIterableStrings(binding.value.node, binding.value.scope, nextSeen, budget)
    }
    return UNRESOLVED
  }

  if (ts.isConditionalExpression(expression)) {
    return combineResolved(
      [
        evaluateStringExpression(expression.whenTrue, scope, seen, budget),
        evaluateStringExpression(expression.whenFalse, scope, seen, budget),
      ],
      budget,
      '字符串条件分支集合合并',
    )
  }

  if (ts.isBinaryExpression(expression) && expression.operatorToken.kind === ts.SyntaxKind.PlusToken) {
    return concatenateResolved(
      evaluateStringExpression(expression.left, scope, seen, budget),
      evaluateStringExpression(expression.right, scope, seen, budget),
      budget,
      '字符串二元拼接',
    )
  }

  if (ts.isTemplateExpression(expression)) {
    let result = resolved([expression.head.text])
    for (const span of expression.templateSpans) {
      result = concatenateResolved(
        result,
        evaluateStringExpression(span.expression, scope, seen, budget),
        budget,
        '模板表达式拼接',
      )
      result = concatenateResolved(result, resolved([span.literal.text]), budget, '模板字面量拼接')
    }
    return result
  }

  if (ts.isPropertyAccessExpression(expression)) {
    return evaluateObjectProperty(expression.expression, expression.name.text, scope, seen, budget)
  }

  if (ts.isElementAccessExpression(expression) && expression.argumentExpression) {
    const propertyNames = evaluateStringExpression(expression.argumentExpression, scope, seen, budget)
    if (!propertyNames.resolved || propertyNames.values.size === 0) {
      return evaluateAllObjectStringValues(expression.expression, scope, seen, budget)
    }
    const propertyValues = [...propertyNames.values].map(propertyName => (
      evaluateObjectProperty(expression.expression, propertyName, scope, seen, budget)
    ))
    return combineResolved(
      propertyValues,
      budget,
      '动态对象属性集合合并',
    )
  }

  return UNRESOLVED
}

function fullMessageKey(namespace, key) {
  return namespace ? `${namespace}.${key}` : key
}

function sourceLine(sourceFile, node) {
  return sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line + 1
}

function normalizedExpression(sourceFile, node) {
  // 保留表达式内部原始空白，避免对模板字面量做全局空白折叠后产生语义碰撞。
  return node ? node.getText(sourceFile).trim() : '<missing argument>'
}

function containingFunctionName(node) {
  for (let current = node.parent; current; current = current.parent) {
    if (ts.isFunctionDeclaration(current) && current.name) return current.name.text
    if ((ts.isFunctionExpression(current) || ts.isArrowFunction(current))
      && ts.isVariableDeclaration(current.parent)
      && ts.isIdentifier(current.parent.name)) return current.parent.name.text
    if (ts.isMethodDeclaration(current) && current.name) return propertyNameText(current.name)
  }
  return null
}

function registeredTranslatorBinding(fileName, node, variable, bindings) {
  const functionName = containingFunctionName(node)
  return bindings.find(binding => binding.file === fileName
    && binding.variable === variable
    && (!binding.functionName || binding.functionName === functionName)) ?? null
}

const CONVENTIONAL_TRANSLATOR_NAMES = new Set([
  't', 'tc', 'et', 'pc', 'rc', 'gc', 'dt', 'rt', 'nl', 'it', 'ts', 'navT', 'cronT',
])

/**
 * 用 TypeScript AST 按词法作用域校验 next-intl 键引用。
 *
 * - 字面量继续逐一校验；
 * - 条件表达式、模板拼接、字符串联合类型及 const 数组 map 属性会展开为有限键集合；
 * - 无法证明为有限集合的动态调用进入 unresolvedDynamicReferences，必须由外部审计清单显式登记。
 */
export function analyzeI18nReferencesInSource(
  source,
  knownKeys,
  fileName = 'source.tsx',
  translatorBindings = [],
  batchBudget,
) {
  const sourceFile = ts.createSourceFile(
    fileName,
    source,
    ts.ScriptTarget.Latest,
    true,
    fileName.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
  )
  const budget = evaluationBudget(fileName, batchBudget)
  const scopeByNode = buildScopes(sourceFile)
  const missing = []
  const expandedDynamicReferences = []
  const unresolvedDynamicReferences = []
  const usedTranslatorBindingIds = new Set()
  const usedKeys = new Set()
  const unboundTranslatorCalls = []
  const inferredTranslatorReferences = []

  function visit(node, parentScope) {
    const scope = scopeByNode.get(node) ?? parentScope
    if (ts.isCallExpression(node)) {
      const target = translatorCallTarget(node)
      if (target) {
        const lexicalTranslator = resolveScopedMap(scope, 'bindings', target.variable)
        const registeredTranslator = registeredTranslatorBinding(fileName, node, target.variable, translatorBindings)
        const translatorValue = lexicalTranslator.value?.kind === 'translator'
          ? lexicalTranslator.value
          : registeredTranslator
            ? {
                kind: 'translator',
                namespace: registeredTranslator.namespace,
                namespaceExpression: registeredTranslator.namespace,
              }
            : null
        if (translatorValue) {
          if (registeredTranslator && lexicalTranslator.value?.kind !== 'translator') {
            usedTranslatorBindingIds.add(registeredTranslator.id)
          }
          const argument = node.arguments[0]
          const literalKey = staticString(argument)
          const evaluated = evaluateStringExpression(argument, scope, new Set(), budget)
          const detail = {
            file: fileName,
            line: sourceLine(sourceFile, node),
            functionName: containingFunctionName(node),
            variable: target.variable,
            method: target.method,
            namespace: translatorValue.namespace,
            namespaceExpression: translatorValue.namespaceExpression,
            expression: normalizedExpression(sourceFile, argument),
          }

          if (translatorValue.namespace === null || !evaluated.resolved || evaluated.values.size === 0) {
            reserveReferenceVolume(budget, 1, `第 ${detail.line} 行存在 1 处未解析 translator 引用`)
            unresolvedDynamicReferences.push(detail)
          } else {
            reserveReferenceVolume(
              budget,
              evaluated.values.size,
              `第 ${detail.line} 行 translator 表达式展开 ${evaluated.values.size} 个引用`,
            )
            const keys = [...evaluated.values].sort()
            if (literalKey === null) expandedDynamicReferences.push({ ...detail, keys })
            for (const key of keys) {
              const fullKey = fullMessageKey(translatorValue.namespace, key)
              usedKeys.add(fullKey)
              if (!knownKeys.has(fullKey)) {
                missing.push({
                  ...detail,
                  namespace: translatorValue.namespace,
                  key,
                  dynamic: literalKey === null,
                })
              }
            }
          }
        } else if (node.arguments.length > 0 && CONVENTIONAL_TRANSLATOR_NAMES.has(target.variable)) {
          // 普通 helper 的 translator 参数没有运行时 namespace 元数据。先收集同一函数内同名参数的
          // 全部调用，再以词典中可同时容纳所有相对键的 namespace 做交集推断。
          const evaluated = evaluateStringExpression(node.arguments[0], scope, new Set(), budget)
          reserveReferenceVolume(
            budget,
            evaluated.resolved && evaluated.values.size > 0 ? evaluated.values.size : 1,
            `helper translator 输入表达式预展开 ${evaluated.values.size || 1} 个引用`,
          )
          unboundTranslatorCalls.push({
            group: `${containingFunctionName(node) ?? '<module>'}:${target.variable}`,
            evaluated,
            detail: {
            file: fileName,
            line: sourceLine(sourceFile, node),
            functionName: containingFunctionName(node),
            variable: target.variable,
            method: target.method,
            namespace: null,
            namespaceExpression: '<未登记 translator helper 参数>',
            expression: normalizedExpression(sourceFile, node.arguments[0]),
            },
          })
        }
      }
    }
    ts.forEachChild(node, child => visit(child, scope))
  }

  visit(sourceFile, null)

  const callsByGroup = new Map()
  for (const call of unboundTranslatorCalls) {
    const calls = callsByGroup.get(call.group) ?? []
    calls.push(call)
    callsByGroup.set(call.group, calls)
  }

  const requestedHelperKeys = new Set()
  for (const call of unboundTranslatorCalls) {
    if (!call.evaluated.resolved) continue
    for (const key of call.evaluated.values) requestedHelperKeys.add(key)
  }
  const candidateNamespaceIndex = buildCandidateNamespaceIndex(knownKeys, requestedHelperKeys, budget)

  function candidateNamespaces(keys) {
    let candidates = null
    for (const key of keys) {
      const matches = candidateNamespaceIndex.get(key) ?? new Set()
      if (candidates === null) {
        candidates = matches
      } else {
        reserveEvaluationOperations(
          budget,
          candidates.size,
          `helper translator 候选 namespace 与相对键 ${key} 求交`,
        )
        candidates = new Set([...candidates].filter(namespace => matches.has(namespace)))
      }
    }
    return candidates ?? new Set()
  }

  for (const calls of callsByGroup.values()) {
    let namespaces = null
    for (const call of calls) {
      const callNamespaces = call.evaluated.resolved && call.evaluated.values.size > 0
        ? candidateNamespaces(call.evaluated.values)
        : new Set()
      if (namespaces === null) {
        namespaces = callNamespaces
      } else {
        reserveEvaluationOperations(
          budget,
          namespaces.size,
          `helper translator 调用组对 ${namespaces.size} 个候选 namespace 求交`,
        )
        namespaces = new Set([...namespaces].filter(namespace => callNamespaces.has(namespace)))
      }
    }

    if (!namespaces || namespaces.size === 0) {
      reserveReferenceVolume(budget, calls.length, `helper translator 有 ${calls.length} 处未解析引用`)
      unresolvedDynamicReferences.push(...calls.map(call => call.detail))
      continue
    }

    for (const call of calls) {
      const keys = [...call.evaluated.values].sort()
      const expandedReferenceCount = namespaces.size * keys.length
      reserveReferenceVolume(
        budget,
        expandedReferenceCount,
        `helper translator 预计展开 ${namespaces.size} 个 namespace × ${keys.length} 个键`,
      )
      for (const namespace of namespaces) {
        for (const key of keys) usedKeys.add(fullMessageKey(namespace, key))
      }
      inferredTranslatorReferences.push({
        ...call.detail,
        namespaces: [...namespaces].sort(),
        keys,
      })
    }
  }

  return {
    missing,
    expandedDynamicReferences,
    unresolvedDynamicReferences,
    usedTranslatorBindingIds,
    usedKeys,
    inferredTranslatorReferences,
  }
}

/** 向后兼容原调用方：返回字面量和可展开动态调用中引用的缺失键。 */
export function findMissingStaticReferencesInSource(source, knownKeys, fileName = 'source.tsx') {
  return analyzeI18nReferencesInSource(source, knownKeys, fileName).missing
}
