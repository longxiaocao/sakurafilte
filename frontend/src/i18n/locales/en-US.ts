/**
 * 国际化语言包 - 英文 (en-US)
 * P2.6: English locale for SakuraFilter
 */
export default {
  admin: {
    etlview: {
      templatetext: {
        l610_: 'rows',
      },
    },
    helpview: {
      string: {
        l19_: ') + Model',
      },
    },
    perfview: {
      templatetext: {
        l203_: '▶ Auto',
      },
    },
    productformview: {
      title: {
        l346_: '① Basic Information',
        l419_: '④ Performance',
        l440_: '⑤ Packaging',
      },
    },
  },
  common: {
    confirm: 'Confirm',
    cancel: 'Cancel',
    save: 'Save',
    delete: 'Delete',
    edit: 'Edit',
    add: 'Add',
    search: 'Search',
    reset: 'Reset',
    back: 'Back',
    loading: 'Loading...',
    retry: 'Retry',
    refresh: 'Refresh',
    export: 'Export',
    import: 'Import',
    copy: 'Copy',
    copied: 'Copied',
    success: 'Operation succeeded',
    failed: 'Operation failed',
    noData: 'No data',
    noResult: 'No matching results',
    loadFailed: 'Load failed, please retry or contact administrator'
  },
  nav: {
    productSearch: 'Product Search',
    oemLookup: 'OEM Lookup',
    productManage: 'Products',
    dictManage: 'Dictionaries',
    userManage: 'Users',
    etlTrigger: 'ETL Trigger',
    compare: 'Compare',
    perf: 'Performance',
    help: 'Help',
    enterAdmin: 'Enter Admin',
    exitAdmin: 'Exit Admin'
  },
  auth: {
    title: 'SakuraFilter',
    subtitle: 'Admin System',
    username: 'Username',
    password: 'Password',
    usernamePlaceholder: 'Enter username',
    passwordPlaceholder: 'Enter password',
    login: 'Login',
    logout: 'Logout',
    changePassword: 'Change Password',
    loginSuccess: 'Login successful',
    loginFailed: 'Login failed, please retry',
    authFailed: 'Invalid username or password',
    userDisabled: 'Account disabled, contact administrator',
    userLocked: 'Account locked, please try later',
    pleaseLogin: 'Please login first',
    defaultAccount: 'Default: admin / (configured at deployment)'
  },
  search: {
    title: 'Product Search',
    placeholder: 'Search OEM / name / model...',
    startSearch: 'Type keyword to start search',
    startSearchDesc: 'Supports OEM number, product name, vehicle model, etc.',
    clickToSearch: 'Click search button or press Enter',
    currentKeyword: 'Current keyword: {q}',
    noResult: 'No products found for {q}, try other keywords',
    clearRetry: 'Clear and retry',
    tolerance: 'Dimension tolerance',
    toleranceDesc: 'Switching tolerance significantly affects search speed (10mm is 5-10x slower than 1mm). Default ±5mm is the balance for most scenarios.',
    tolerance1: '±1mm (precise)',
    tolerance5: '±5mm (recommended)',
    tolerance10: '±10mm (loose)',
    resultCount: '{total} results (tolerance ±{tol}mm)',
    showingFirst: '(showing first {n})',
    provider: 'Provider: {provider}',
    batchTitle: 'Batch Search',
    singleTitle: 'Single Search'
  },
  product: {
    published: 'Published',
    discontinued: 'Discontinued',
    basicInfo: 'Basic Info',
    dimensions: 'Dimensions',
    performance: 'Performance',
    packaging: 'Packaging',
    crossReference: 'Cross Reference',
    machineApp: 'Machine Application',
    gallery: 'Gallery',
    spec: 'Spec'
  },
  theme: {
    toggle: 'Theme toggle',
    light: 'Light',
    dark: 'Dark',
    switchToLight: 'Switch to light',
    switchToDark: 'Switch to dark'
  },
  error: {
    title: 'Page failed to load',
    desc: 'An unexpected error occurred. Try the following actions',
    copyError: 'Copy error',
    refreshPage: 'Refresh page',
    technicalDetails: 'Technical details',
    timestamp: 'Time'
  },
  a11y: {
    skipToContent: 'Skip to main content'
  }
}
