/**
 * 统一封装 AbortController 生命周期的高阶函数
 *
 * WHY: 多个组件 (SearchView/AdminProductsView) 都手动管理 AbortController,
 *      代码重复且容易遗漏 onBeforeUnmount 清理。withAbort 提供声明式 API:
 *      - 自动管理 controller 实例
 *      - 调用前自动 abort 上一次请求 (避免竞态)
 *      - 组件卸载时自动 abort (防内存泄漏)
 *      - 静默 ERR_CANCELED 错误 (不弹出错误提示)
 *
 * 用法:
 *   const { run, cancel } = withAbort(searchApi.search)
 *   run({ keyword: 'abc' })  // 自动透传到 searchApi.search 的第二参数 { signal }
 *   cancel()  // 手动取消
 *   // 组件卸载时自动 abort (通过 onBeforeUnmount 注册)
 *
 * 设计约束:
 *   - 仅适用于签名 (payload: P, config?: { signal?: AbortSignal }) => Promise<R> 的 API 函数
 *   - run 返回 Promise<R | null>: 取消时返回 null, 不抛错 (调用方需处理 null)
 *   - ERR_CANCELED 静默: 与 utils/http.ts 拦截器行为一致, 不弹错误提示
 */
import { onBeforeUnmount } from 'vue'
import axios, { AxiosError } from 'axios'

type ApiFn<P, R> = (payload: P, config?: { signal?: AbortSignal }) => Promise<R>

export interface WithAbortResult<P, R> {
  /** 发起请求, 自动 abort 上一次; 取消时返回 null, 不抛错 */
  run: (payload: P) => Promise<R | null>
  /** 手动取消当前进行中的请求 */
  cancel: () => void
}

/**
 * 高阶函数: 包装一个支持 signal 的 API 函数, 自动管理 AbortController 生命周期
 *
 * @param apiFn 目标 API 函数, 签名 (payload, config?) => Promise<R>
 * @returns { run, cancel } - run 发起请求, cancel 手动取消
 */
export function withAbort<P, R>(apiFn: ApiFn<P, R>): WithAbortResult<P, R> {
  let controller: AbortController | null = null

  const cancel = () => {
    if (controller) {
      controller.abort()
      controller = null
    }
  }

  // 组件卸载时自动取消, 防内存泄漏
  // 注: onBeforeUnmount 必须在 setup 同步调用期内执行 withAbort, 否则不会注册
  onBeforeUnmount(() => cancel())

  const run = async (payload: P): Promise<R | null> => {
    // 调用前 abort 上一次请求, 避免竞态 (快速切换筛选条件时旧响应覆盖新响应)
    cancel()
    controller = new AbortController()
    try {
      return await apiFn(payload, { signal: controller.signal })
    } catch (err) {
      // ERR_CANCELED 静默: 与 utils/http.ts 拦截器一致, 不弹错误提示
      // 兼容: axios.isCancel (CancelError) + err.code === 'ERR_CANCELED' (AxiosError) + err.name === 'CanceledError'
      if (
        axios.isCancel(err) ||
        (err as AxiosError)?.code === 'ERR_CANCELED' ||
        (err as { name?: string })?.name === 'CanceledError'
      ) {
        return null
      }
      throw err
    } finally {
      // 请求完成 (成功/失败) 后清理引用, 避免误 abort 已完成的请求
      if (controller?.signal.aborted) {
        controller = null
      }
    }
  }

  return { run, cancel }
}
