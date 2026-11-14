import { createHash } from 'node:crypto'

/**
 * 503 页内联脚本：点「重试」立刻进入 pending，挡住连点；
 * 若整页导航 8s 仍未离开（上游仍超时），恢复可点并换成「仍不可用…」。
 * 字节必须稳定——CSP script-src 用它的 sha256。改脚本后单测会锁新哈希。
 */
export const SESSION_UNAVAILABLE_RETRY_SCRIPT =
  '(function(){var a=document.getElementById("retry");if(!a)return;var h=document.getElementById("hint");var t=null;a.addEventListener("click",function(e){if(a.getAttribute("aria-busy")==="true"){e.preventDefault();return}a.setAttribute("aria-busy","true");a.classList.add("is-pending");a.textContent=a.getAttribute("data-pending")||a.textContent;if(h)h.hidden=false;if(t)clearTimeout(t);t=setTimeout(function(){a.removeAttribute("aria-busy");a.classList.remove("is-pending");a.textContent=a.getAttribute("data-again")||a.getAttribute("data-idle")||a.textContent;if(h)h.hidden=true},8000)})})();'

export function sessionUnavailableScriptSrcHash(source = SESSION_UNAVAILABLE_RETRY_SCRIPT): string {
  return `'sha256-${createHash('sha256').update(source, 'utf8').digest('base64')}'`
}
