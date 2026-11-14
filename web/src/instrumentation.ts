export async function register() {
  // 按 Next 16 内置文档隔离 Node-only API，避免 process.exit / undici 被打进 Edge bundle。
  if (process.env.NEXT_RUNTIME === 'nodejs') {
    const { registerNodeInstrumentation } = await import(
      './instrumentation-node'
    )
    await registerNodeInstrumentation()
  }
}
