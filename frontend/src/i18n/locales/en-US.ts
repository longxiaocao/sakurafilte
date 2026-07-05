/**
 * 国际化语言包 - 英文 (en-US)
 * P2.6: English locale for SakuraFilter
 */
export default {
  admin: {
    compareview: {
      placeholder: {
        l332_id: 'Input Product ID Add',
      },
      string: {
        l102_kg: 'Weight (kg)',
        l103_mm: 'carton 长 (mm)',
        l104_mm: 'carton 宽 (mm)',
        l105_mm: 'carton 高 (mm)',
        l106_m: 'carton Volume (m³)',
        l113_kg: 'Outer Carton 重 (kg)',
        l114_mm: 'Outer Carton 长 (mm)',
        l115_mm: 'Outer Carton 宽 (mm)',
        l116_mm: 'Outer Carton 高 (mm)',
        l120_crossref: 'CrossRef / Vehicle Model',
        l61_1: 'Product Name 1',
        l62_2: 'Product Name 2',
        l67_mm: 'Dimensions (mm)',
        l86_lr: 'Bypass LR',
        l87_hr: 'Bypass HR',
        l88_1: 'Efficiency 1',
        l89_2: 'Efficiency 2',
        l91_bar: 'Pressure Resistance (bar)',
      },
      warning: {
        l256_id: 'Please enter Active Product ID',
      },
    },
    enginesview: {
      placeholder: {
        l159_cummins: 'e.g.: CUMMINS',
        l162_isb_4_5_l: 'e.g.: ISB 4.5 L (可Empty)',
      },
    },
    etlview: {
      label: {
        l605_json: 'Original JSON',
      },
      placeholder: {
        l472_jsonl: 'JSONL Absolute path',
      },
      string: {
        l275_etl: 'Cancel ETL Task',
        l354_: 'Cancel',
        l355_etl: 'Pause ETL Task',
        l378_etl_n_n_paused_checkpoint_id_1_commit: 'Resume Pause ETL Task?\\n\\nwill from Recent 一 items paused Record checkpoint_id+1 rows Start 续读, Skip COMMIT batch times.',
        l379_etl: 'Resume ETL Task',
        l423_insert: 'INSERT Write DB',
        l484_truncate_xrefs_apps_products: 'On 启: TRUNCATE 同时 Clear xrefs/apps (首times 全Count 场景); Close: Only 清 products, 保留Off 联Table (单独 Refresh 主Table)',
        l81_etl: '松On 以填入 ETL File 路径',
      },
      templatetext: {
        l495_dry_run: 'Execute dry-run',
        l613_: 'Expand All',
        l613_10: 'Collapse (只Show Front 10 rows)',
      },
    },
    helpview: {
      string: {
        l127_xlsx: '拖拽 XLSX to 此',
        l15_cross_references_oem_brand_mann_bosch_ma: 'Alternative Brand 厂家名 (cross_references.oem_brand), e.g.: Mann, Bosch, Mahle',
        l16_1: 'Product Name 1',
        l16_oil_filter_fuel_filter: 'Product 主Name (e.g.: Oil Filter, Fuel Filter), 影响frontend Product',
        l17_2: 'Product Name 2',
        l18_5_oil_fuel_air_cabin_others_sort_order: '5 固定Category: oil / fuel / air / cabin / others, sort_order 决定frontend Sort Order',
        l18_type: 'Type (Type)',
        l19_oem_5_27m_distinct_typeahead: 'Alternative Brand OEM Number (5.27M distinct), 字典化便于 typeahead 联想',
        l20_media: 'Media (Media)',
        l21_machine: 'Machine Model (Machine)',
        l22_engine: 'Engine (Engine)',
        l29_oem_oem2_cross_references_oem_brand_oemn: '检查该 OEM Yes No in Product Table oem2 Field 里 (注意: 不Yes cross_references.oem_brand). frontend Published 用 oemNoDisplayt',
        l32_typeahead: 'for 什么 Add Product 时 typeahead 联想不to 想要 Value?',
        l33_r: '[EN] )r 排).',
        l36_h1_100_0: 'Dimensions Search (H1=100) Back 0 items Result, but 库里有这pcs Product?',
        l41_reading_copy_1m_30_60s_5_output_spike_re: 'reading Stage Yes 流式 COPY Staging, 大File (1M rows) 可能 30-60s. 如超过 5 min 无Progress, 检查Back 端Log (output/SPIKE-RE',
        l49_1_ispublished_true_2_slot_1_6_3_console_: '检查 (1) Product isPublished=true (Listed) (2) slot 1-6 范围 (3) Browser console 看 OSS 预Signature URL 1h',
      },
    },
    machinesview: {
      placeholder: {
        l198_bosch: 'e.g.: BOSCH',
        l201_0_451_103_001: 'e.g.: 0 451 103 001 (可Empty)',
        l204_tractor_x300: 'e.g.: Tractor X300 (可Empty)',
        l209_4: 'Select 4 大类之一',
      },
    },
    mediasview: {
      label: {
        l159_media: 'Media Name',
        l162_media: 'Media Model',
      },
      placeholder: {
        l160_cellulose_synthetic_carbon: 'e.g.: Cellulose / Synthetic / Carbon',
      },
      title: {
        l157_media: 'Add Media',
        l157_media_2: 'Edit Media',
      },
    },
    oembrandsview: {
      placeholder: {
        l300_bosch: 'e.g.: BOSCH',
      },
      string: {
        l98_: '吗? (软 Delete, 可in',
      },
      title: {
        l295_oem: 'Add OEM Brand',
        l295_oem_2: 'Edit OEM Brand',
      },
    },
    oemno3sview: {
      placeholder: {
        l151_11427622448: 'e.g.: 11427622448',
      },
      title: {
        l148_oem_3: 'Add OEM 3',
        l148_oem_3_2: 'Edit OEM 3',
      },
      warning: {
        l47_oem_3_200: 'OEM 3 Length 不能超过 200',
      },
    },
    productformview: {
      error: {
        l174_oem: 'Product already exists, Please check the OEM number',
      },
      label: {
        l352_1: 'Product Name 1',
        l356_2: 'Product Name 2',
        l431_1: 'Efficiency 1',
        l432_2: 'Efficiency 2',
        l433_lr: 'Bypass Valve LR',
        l434_hr: 'Bypass Valve HR',
        l436_bar: 'Collapse Pressure (bar)',
        l449_kg: 'Weight (kg)',
        l453_mm: 'carton 长 (mm)',
        l457_mm: 'carton 宽 (mm)',
        l461_mm: 'carton 高 (mm)',
        l466_m: 'carton Volume (m³)',
        l481_kg: 'Master Carton 重 (kg)',
        l485_mm: 'Master Carton 长 (mm)',
        l489_mm: 'Master Carton 宽 (mm)',
        l493_mm: 'Master Carton 高 (mm)',
        l497_m: 'Master Box Volume (m³)',
      },
      placeholder: {
        l427_name_model_or: 'Input Auto 补全 (name/model OR 匹配)',
      },
      title: {
        l404_mm: '③ Dimensions (mm)',
        l536_1_6: '⑦ Image (1-6 槽位)',
      },
    },
    productname1sview: {
      label: {
        l234_1: 'Product Name 1',
      },
      placeholder: {
        l235_oil_filter: 'e.g.: OIL FILTER',
      },
      string: {
        l84_: '吗? (软 Delete, 可in',
      },
      title: {
        l232_1: 'Add Product 名 1',
        l232_1_2: 'Edit Product 名 1',
      },
      warning: {
        l61_1: 'Product Name 1 cannot be empty',
        l62_1_200: 'Product Name 1 Length 不能超过 200',
      },
    },
    productname2sview: {
      label: {
        l150_2: 'Product Name 2',
      },
      placeholder: {
        l151_spin_on: 'e.g.: SPIN-ON',
      },
      title: {
        l148_2: 'Add Product 名 2',
        l148_2_2: 'Edit Product 名 2',
      },
      warning: {
        l46_2: 'Product Name 2 cannot be empty',
        l47_2_200: 'Product Name 2 Length 不能超过 200',
      },
    },
    productsview: {
      placeholder: {
        l306_oem_3: 'OEM 3 batch Count',
        l392_1: 'Product Name 1',
        l393_2: 'Product Name 2',
      },
      warning: {
        l237_2_6: 'Please select 2-6 pcs Product',
        l241_6: 'At most Compare 6 pcs',
      },
    },
    typesview: {
      placeholder: {
        l161_oil_fuel_air_cabin_others: 'e.g.: oil / fuel / air / cabin / others',
      },
      title: {
        l158_type: 'Add Type',
        l158_type_2: 'Edit Type',
      },
      warning: {
        l49_type_50: 'Type Length 不能超过 50',
      },
    },
    usersview: {
      placeholder: {
        l396_8: 'At least 8 pcs 字符',
        l463_8: 'At least 8 pcs 字符',
      },
      string: {
        l59_admin: 'Admin (admin)',
        l60_operator: 'Action 员 (operator)',
        l61_viewer: 'Read-only (viewer)',
      },
      warning: {
        l106_8: 'Password At least 8 pcs 字符',
        l194_8: '新Password At least 8 pcs 字符',
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
