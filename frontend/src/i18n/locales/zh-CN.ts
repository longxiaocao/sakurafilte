/**
 * 国际化语言包 - 中文 (zh-CN)
 * P2.6: 基础国际化设施, 覆盖核心 UI 文案
 *
 * 结构:
 *   - common: 通用动作 (确认/取消/保存/删除等)
 *   - nav: 导航菜单
 *   - auth: 登录/认证
 *   - search: 搜索相关
 *   - product: 产品详情
 *   - theme: 主题切换
 */
export default {
  common: {
    confirm: '确认',
    cancel: '取消',
    save: '保存',
    delete: '删除',
    edit: '编辑',
    add: '新增',
    search: '搜索',
    reset: '重置',
    back: '返回',
    loading: '加载中...',
    retry: '重试',
    refresh: '刷新',
    export: '导出',
    import: '导入',
    copy: '复制',
    copied: '已复制',
    success: '操作成功',
    failed: '操作失败',
    noData: '暂无数据',
    noResult: '未找到匹配结果',
    loadFailed: '加载失败, 请稍后重试或联系管理员'
  },
  nav: {
    productSearch: '产品搜索',
    oemLookup: 'OEM 查询',
    productManage: '产品管理',
    dictManage: '字典管理',
    userManage: '用户管理',
    etlTrigger: 'ETL 触发',
    compare: '产品对比',
    perf: '性能',
    help: '帮助',
    enterAdmin: '进入后台',
    exitAdmin: '退出后台'
  },
  auth: {
    title: 'SakuraFilter',
    subtitle: '后台管理系统',
    username: '用户名',
    password: '密码',
    usernamePlaceholder: '请输入用户名',
    passwordPlaceholder: '请输入密码',
    login: '登录',
    logout: '退出登录',
    changePassword: '修改密码',
    loginSuccess: '登录成功',
    loginFailed: '登录失败, 请稍后重试',
    authFailed: '用户名或密码错误',
    userDisabled: '账号已被禁用, 请联系管理员',
    userLocked: '账号已锁定, 请稍后重试',
    pleaseLogin: '请先登录',
    defaultAccount: '默认账号: admin / (部署时配置)'
  },
  search: {
    title: '产品搜索',
    placeholder: '搜索 OEM / 名称 / 车型...',
    startSearch: '输入关键词开始搜索',
    startSearchDesc: '支持 OEM 编号、产品名、车型等',
    clickToSearch: '点击搜索按钮或按回车查询',
    currentKeyword: '当前关键词: {q}',
    noResult: '未找到与 {q} 相关的产品, 请尝试其他关键词',
    clearRetry: '清空重试',
    tolerance: '尺寸容差',
    toleranceDesc: '切换容差会显著影响搜索速度 (10mm 比 1mm 慢 5-10 倍), 默认 ±5mm 是大多数场景的平衡点。',
    tolerance1: '±1mm (精确)',
    tolerance5: '±5mm (推荐)',
    tolerance10: '±10mm (宽松)',
    resultCount: '共 {total} 条结果 (容差 ±{tol}mm)',
    showingFirst: '(显示前 {n} 条)',
    provider: '搜索引擎: {provider}',
    batchTitle: '批量搜索',
    singleTitle: '单条搜索'
  },
  product: {
    published: '已发布',
    discontinued: '已停售',
    basicInfo: '基础信息',
    dimensions: '尺寸规格',
    performance: '性能参数',
    packaging: '包装信息',
    crossReference: '替代 OEM',
    machineApp: '适配车型',
    gallery: '图片画廊',
    spec: '规格'
  },
  theme: {
    toggle: '主题切换',
    light: '浅色',
    dark: '深色',
    switchToLight: '切换到浅色',
    switchToDark: '切换到深色'
  },
  error: {
    title: '页面加载失败',
    desc: '系统遇到了意外错误, 可尝试以下操作',
    copyError: '复制错误',
    refreshPage: '刷新页面',
    technicalDetails: '查看技术详情',
    timestamp: '时间'
  },
  a11y: {
    skipToContent: '跳到主内容'
  }
}
