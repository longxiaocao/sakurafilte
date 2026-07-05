/**
 * 国际化语言包 - 英文 (en-US)
 * P2.6: English locale for SakuraFilter
 */
export default {
  admin: {
    compareview: {
      error: {
        l273_: 'Load Product Failed',
      },
      placeholder: {
        l329_id: 'Input Product ID Add',
      },
      string: {


        l107_: 'Outer Carton',
        l109_: 'Outer Carton/pcs',
        l110_kg: 'Outer Carton 重 (kg)',
        l111_mm: 'Outer Carton 长 (mm)',
        l112_mm: 'Outer Carton 宽 (mm)',
        l113_mm: 'Outer Carton 高 (mm)',
        l117_crossref: 'CrossRef / Vehicle Model',
        l119_oem: 'OEM Cross-Reference',
        l120_: 'Machine Applications',
        l185_: 'Load failed',
        l53_: 'Basic',
        l55_oem: 'OEM Number',



        l64_mm: 'Dimensions (mm)',


        l83_lr: 'Bypass LR',
        l84_hr: 'Bypass HR',



        l88_bar: 'Pressure Resistance (bar)',


        l96_: 'Media',
        l97_: 'Media Model',


      },
      success: {
        l246_: 'Remove',
        l272_added: 'Added: {oem}',
      },
      title: {
        l373_: 'Move Left',
        l381_: 'Move Right',
        l390_: 'Remove 该columns',
      },
      warning: {
        l253_id: 'Please enter Active Product ID',
        l257_: '该Product in Compare List',
        l262_max: 'Compare Max {max} Products',
      },
    },
    enginesview: {
      error: {





      },
      label: {



      },
      placeholder: {

        l156_cummins: 'e.g.: CUMMINS',
        l159_isb_4_5_l: 'e.g.: ISB 4.5 L (可Empty)',
      },
      string: {

      },
      success: {





      },
      title: {
        l153_: 'Add Engine',
        l153__2: 'Edit Engine',
      },
      warning: {
        l47_: 'Engine Brand cannot be empty',
        l48_200: 'Engine Brand Length 不能超过 200',

      },
    },
    etlview: {
      buttontext: {
        l290_: 'Next',

        l306_: 'Confirm Cancel',

        l353_: 'Pause',
        l353__2: '不Pause',

        l377__2: '不Resume',
      },
      info: {
        l305_: '可补充详细Description (留Empty 用Default)',
        l305__2: 'Cancel 原因Note',

        l362_: '无活跃Task 可Pause',
      },
      label: {
        l450_: 'Entity',

        l466_: 'File 路径',
        l592_: 'File',
        l593_: '[EN] 大小',
        l594_: 'rows Count',

        l602_json: 'Original JSON',
        l648_: 'Timestamp',
        l649_: 'Error',
        l682_: '[EN] 原因',
        l683_: '读/插/改',
        l690_: '[EN] 耗时',
        l695_: 'Cancel Timestamp',
      },
      placeholder: {
        l469_jsonl: 'JSONL Absolute path',
      },
      string: {
        l219_sse: 'SSE 连接断On, Browser will Auto 重连',




        l258_: 'Task timeout',
        l258__2: 'Task Execute 超时',
        l259_: 'System shutdown/Restart',
        l259__2: 'Service Close/Restart',


        l272_etl: 'Cancel ETL Task',


        l352_etl: 'Pause ETL Task',

        l353_pause_msg: 'Pause Current ETL Task?\n\nCurrent batch will Exit gracefully after Complete, checkpoint_id will Write etl_progress_log, the Follow "{resume}" button can be used to 续读 from 该 point.\n\n(Different from "{cancel}" — Cancel will Immediately Terminate and Rollback Current batch)',

        l375_etl_n_n_paused_checkpoint_id_1_commit: 'Resume Pause ETL Task?\\n\\nwill from Recent 一 items paused Record checkpoint_id+1 rows Start 续读, Skip COMMIT batch times.',
        l376_etl: 'Resume ETL Task',
        l386_resume: 'Resume Triggered: entity={entity} checkpoint={checkpoint} (from line {line})',
        l386_resume_alt: 'Resume Triggered: entity={entity} checkpoint={checkpoint} (from line {line} Continue)',
        l419_copy: 'COPY Staging',
        l420_insert: 'INSERT Write DB',
        l421_commit: 'COMMIT Submit',
        l422_meili: 'Meili Sync',
        l423_: 'Complete',
        l481_truncate_xrefs_apps_products: 'On 启: TRUNCATE 同时 Clear xrefs/apps (首times 全Count 场景); Close: Only 清 products, 保留Off 联Table (单独 Refresh 主Table)',
        l64_auto_inferred: 'Auto-recognized entity={entity}, file: {name}',
        l68_manual_entity: 'File Filled: {name} (entity need manual select)',
        l71_first_only: 'Dropped {total} files, only first used: {name}',
        l78_etl: '松On 以填入 ETL File 路径',

        l323_cancel_signal: 'Cancel Signal Sent (code: {code}), task will Terminate soon',
      },
      success: {
        l111_dry_run: 'dry-run Validation completed',
        l111_etl: 'Triggered ETL, background Execute',
        l249_: '清除',
      },
      templatetext: {
        l492_: 'Immediately Import',
        l492_dry_run: 'Execute dry-run',
        l610_: 'Expand All',
        l610_10: 'Collapse (只Show Front 10 rows)',
      },
      warning: {

      },
    },
    helpview: {
      string: {
        l124_xlsx: '拖拽 XLSX to 此',
        l127_: 'Search',
        l12_cross_references_oem_brand_mann_bosch_ma: 'Alternative Brand 厂家名 (cross_references.oem_brand), e.g.: Mann, Bosch, Mahle',

        l13_oil_filter_fuel_filter: 'Product 主Name (e.g.: Oil Filter, Fuel Filter), 影响frontend Product',

        l14_of100: 'Product 副Name/Model Back 缀 (e.g.: OF100)',
        l15_5_oil_fuel_air_cabin_others_sort_order: '5 固定Category: oil / fuel / air / cabin / others, sort_order 决定frontend Sort Order',
        l15_type: 'Type (Type)',
        l16_oem_5_27m_distinct_typeahead: 'Alternative Brand OEM Number (5.27M distinct), 字典化便于 typeahead 联想',
        l17_2_cellulose_a020: 'Filter Media Name + Model (2 Field 字典), e.g.: Cellulose / A020',
        l17_media: 'Media (Media)',
        l18_4_agriculture_commercial_construction_ot: 'Machine Brand + Model + Name, 按 4 大类聚合: Agriculture / Commercial / Construction / others',
        l18_machine: 'Machine Model (Machine)',
        l19_: 'Engine Brand + Model',
        l19_engine: 'Engine (Engine)',
        l25_oem: 'for 什么Input OEM Number Back 无法 Search?',
        l25_a_oem: 'Check if 该 OEM is in products.oem2 field (Note: not cross_references.oem_brand). Public Page uses oemNoDisplay / oem2, Admin Search uses any field.',
        l26_oem_oem2_cross_references_oem_brand_oemn: '检查该 OEM Yes No in Product Table oem2 Field 里 (注意: 不Yes cross_references.oem_brand). frontend Published 用 oemNoDisplay',
        l29_typeahead: 'for 什么 Add Product 时 typeahead 联想不to 想要 Value?',
        l29_a_typeahead: 'Dictionary is maintained in admin, need to add value in "Dictionary Management" → target dict → Add. typeahead only returns existing values (top 20 by sort_order).',
        l30_: 'Dictionary Management',
        l33_h1_100_0: 'Dimensions Search (H1 = 100) Back 0 items Result, but 库里有这pcs Product?',
        l34_5mm_95_105_h1_110_h1_id: 'Dimensions Search Default 容差 ±5mm (固定, 不可改), 即 95-105 之间. 如果Product H1 = 110, 不会命. 改用更小 H1 Value or Precise ID Query.',
        l37_etl_reading: 'ETL Trigger Back 卡in reading Status?',
        l37_a_etl: 'reading phase is streaming COPY staging, large files (1M rows) may take 30-60s. If no progress after 5 minutes, check backend log (output/SPIKE-REPORT-*.md) for SQL errors.',
        l41_: '怎么 Batch Delete Product?',
        l41_a_batch: 'In admin product list, select multiple rows → top "Batch Discontinue" button. Discontinue = is_discontinued=true, hidden on public page, history preserved. For physical delete, use SQL (carefully).',
        l45_: 'Upload Image Back frontend 不Show?',
        l45_a_image: 'Check (1) product isPublished=true (2) slot 1-6 range (3) browser console for OSS pre-signed URL 1h validity. If expired, reload product page.',
        l46_1_ispublished_true_2_slot_1_6_3_console_: '检查 (1) Product isPublished=true (Listed) (2) slot 1-6 范围 (3) Browser console 看 OSS 预Signature URL 1',
        l82_: 'Enter Admin',
        l84_full_load_insert_only_upsert: '+ Mode (full-load / insert-only / upsert), 点',
      },
      title: {
        l71_: '快速Start',
        l72_: '[EN] 字典使用规范',
        l73_: 'Batch Import',
        l74_: 'Search 容差',
        l75_: 'Common 问题',
      },
    },
    machinesview: {
      error: {





      },
      label: {



        l205_: 'Category',

      },
      placeholder: {

        l198_0_451_103_001: 'e.g.: 0 451 103 001 (可Empty)',
        l201_tractor_x300: 'e.g.: Tractor X300 (可Empty)',
        l206_4: 'Select 4 大类之一',
      },
      string: {

      },
      success: {





      },
      title: {
        l192_: 'Add Machine Model',
        l192__2: 'Edit Machine Model',
      },
      warning: {
        l63_: 'Machine Model Brand cannot be empty',
        l64_200: 'Machine Model Brand Length 不能超过 200',

      },
    },
    mediasview: {
      error: {





      },
      label: {
        l156_media: 'Media Name',
        l159_media: 'Media Model',

      },
      placeholder: {
        l110_media: 'Search Media Name or Model',
        l157_cellulose_synthetic_carbon: 'e.g.: Cellulose / Synthetic / Carbon',
        l160_5_m_10_m: 'e.g.: 5μm / 10μm (可Empty)',
      },
      string: {

      },
      success: {





      },
      title: {
        l154_media: 'Add Media',
        l154_media_2: 'Edit Media',
      },
      warning: {
        l49_media: 'Media Name cannot be empty',
        l50_media_100: 'Media Name Length 不能超过 100',

      },
    },
    oembrandsview: {
      error: {

      },
      label: {


      },
      placeholder: {
        l212_: 'Search Brand',

      },
      string: {



        l280_: 'Add Brand',


      },
      success: {





      },
      title: {

        l292_oem: 'Add OEM Brand',
        l292_oem_2: 'Edit OEM Brand',
      },
      warning: {
        l65_: 'Brand 名 cannot be empty',
        l69_100: 'Brand 名 Length 不能超过 100',
      },
    },
    oemno3sview: {
      error: {





      },
      label: {

      },
      placeholder: {
        l103_oem_3: 'Search OEM 3',
        l148_11427622448: 'e.g.: 11427622448',
      },
      string: {

      },
      success: {





      },
      title: {
        l145_oem_3: 'Add OEM 3',
        l145_oem_3_2: 'Edit OEM 3',
      },
      warning: {
        l43_oem_3: 'OEM 3 cannot be empty',
        l44_oem_3_200: 'OEM 3 Length 不能超过 200',

      },
    },
    perfview: {
      label: {
        l201_: 'Pause Auto Refresh',
        l201__2: 'On 启Auto Refresh',
        l209_: 'Refresh 间隔',
      },
      string: {
        l150_p95_crit: 'P95 = {ms}ms (≥1000ms Critical)',
        l152_p95_warn: 'P95 = {ms}ms (≥500ms Warning)',
        l155_err_crit: 'Error Rate = {pct}% (≥10% Critical)',
        l157_err_warn: 'Error Rate = {pct}% (≥5% Warning)',

        l165_: '[EN] 就绪',
        l166_: 'Downgrade',

        l84_: 'Refresh Failed',
      },
      templatetext: {
        l203_: '⏸ Pause',
        l221_: 'Refresh …',
        l221__2: '↻ Refresh',
        l240_: '⚠ 严重Alert',
        l240__2: '⚠ Warning',

        l303__2: '[EN] 存活',

        l336_appsettings_json: '[EN] appsettings.json (兜底)',
        l336_db: 'DB ( Load)',
      },
    },
    productformview: {
      error: {
        l165_: 'Data has been modified by another admin, Please refresh and retry',
        l171_oem: 'Product already exists, Please check the OEM number',



      },
      label: {



        l362_oem_2: 'OEM 2 (Required)',

        l366_: 'Remark',




        l430_lr: 'Bypass Valve LR',
        l431_hr: 'Bypass Valve HR',

        l433_bar: 'Collapse Pressure (bar)',



        l474_: 'Master Box Qty',
        l478_kg: 'Master Carton 重 (kg)',
        l482_mm: 'Master Carton 长 (mm)',
        l486_mm: 'Master Carton 宽 (mm)',
        l490_mm: 'Master Carton 高 (mm)',
        l494_m: 'Master Box Volume (m³)',
      },
      placeholder: {


        l379_: 'Brand (Input Auto 补全)',
        l392_oem_3: 'OEM 3 (Input Auto 补全)',

        l424_name_model_or: 'Input Auto 补全 (name/model OR 匹配)',


        l512_: 'Brand (Required)',
        l516_: 'Model (Required)',


        l525_: 'Engine Model',
      },
      string: {
        l164_: 'by Modify',
        l164__2: 'by 其他User Modify',
        l291_slot_uploaded: 'Slot {slot} Uploaded',
        l312_slot_deleted: 'Slot {slot} Deleted',
        l340_edit_product: 'Edit Product #{id}',
        l375_xrefs: '② Cross-Reference ({count})',
        l510_apps: '⑥ Machine Applications ({count})',
      },
      success: {
        l148_: 'Saved',
        l151_: 'Created',
      },
      templatetext: {
        l338_: 'Add Product',
      },
      title: {
        l346_: 'Basic Info',
        l401_mm: '③ Dimensions (mm)',

        l533_1_6: '⑦ Image (1-6 槽位)',
      },
      warning: {
        l282_: 'Please first Save Product then Upload Image',
      },
    },
    productname1sview: {
      error: {

      },
      label: {


      },
      placeholder: {
        l176_1: 'Search Product Name 1',
        l232_oil_filter: 'e.g.: OIL FILTER',
      },
      string: {





      },
      success: {





      },
      title: {

        l229_1: 'Add Product 名 1',
        l229_1_2: 'Edit Product 名 1',
      },
      warning: {
        l58_1: 'Product Name 1 cannot be empty',
        l59_1_200: 'Product Name 1 Length 不能超过 200',
      },
    },
    productname2sview: {
      error: {





      },
      label: {

      },
      placeholder: {
        l103_2: 'Search Product Name 2',
        l148_spin_on: 'e.g.: SPIN-ON',
      },
      string: {

      },
      success: {





      },
      title: {
        l145_2: 'Add Product 名 2',
        l145_2_2: 'Edit Product 名 2',
      },
      warning: {
        l43_2: 'Product Name 2 cannot be empty',
        l44_2_200: 'Product Name 2 Length 不能超过 200',

      },
    },
    productsview: {
      aria: {
        l297_oem2: 'OEM 2 Search',
        l298_mr1: 'MR.1 Search',
        l299_product_name: 'Product Name Search',
        l300_type: 'Filter by Type',
        l307_oem3_batch: 'OEM 3 Batch Search',
      },
      label: {



        l349_: 'Discontinued',
        l354_: 'Update',
        l357_: 'Action',

        l513_: 'Field',
        l514_: '新Value',
      },
      placeholder: {

        l303_oem_3: 'OEM 3 batch Count',



        l397_: 'Efficiency',




      },
      string: {
        l309_: 'All columns',
        l309__2: '核心columns',
      },
      success: {
        l157_: 'Discontinued',

      },
      title: {
        l385_: '高级Filter',
        l435_: '[EN] 变更历史',
      },
      warning: {

        l234_2_6: 'Please select 2-6 pcs Product',
        l238_6: 'At most Compare 6 pcs',
      },
    },
    typesview: {
      error: {





      },
      label: {

      },
      placeholder: {
        l113_type: 'Search Type',
        l158_oil_fuel_air_cabin_others: 'e.g.: oil / fuel / air / cabin / others',
      },
      string: {


      },
      success: {




        l88_p2_3: 'Sort Order Saved, frontend Product P2.3 Immediately take effect',
      },
      title: {
        l155_type: 'Add Type',
        l155_type_2: 'Edit Type',
      },
      warning: {
        l45_type: 'Type cannot be empty',
        l46_type_50: 'Type Length 不能超过 50',
      },
    },
    usersview: {
      label: {
        l284_: 'User List',
        l342_: 'Login Audit',

        l389_: 'Password',



        l443_: 'Enable Status',
        l456_: '新Password',
      },
      placeholder: {
        l387_: 'Login Username',


      },
      string: {

        l199_reset_pwd: 'Password of {user} has been Reset',
        l56_admin: 'Admin (admin)',
        l57_operator: 'Action 员 (operator)',
        l58_viewer: 'Read-only (viewer)',
      },
      success: {
        l115_: 'User Created',
        l147_: 'User Updated',

        l252_: 'Logout',
      },
      title: {
        l384_: 'Add User',
        l424_edit_user: 'Edit User: {user}',
        l456_reset_pwd: 'Reset Password: {user}',
      },
      warning: {
        l103_8: 'Password At least 8 pcs 字符',
        l191_8: '新Password At least 8 pcs 字符',
        l99_: 'Username cannot be empty',
      },
    },
  },

  common: {

    field: {

      soft_delete_confirm: '吗? (软 Delete, 可in',

      slot_must_be_1_to_6: ', must be between 1-6',

      d7_thread: 'D7 Thread',

      d8_thread: 'D8 Thread',

      oem_brand: 'OEM Brand',

      invalid_slot: 'Invalid Slot:',

      no_cancel: '不 Cancel',

      unlimited: '[EN] 不限',

      product_name: 'Product Name',

      e_g_bosch: 'e.g.: BOSCH',

      full_name: '[EN] 全名',

      all: 'All',

      other_reason: 'Other reason',

      packaging: 'Packaging',

      check_valve_count: 'Check Valve Count',

      engine_brand: 'Engine Brand',

      publish: 'Publish',

      cancel: 'Cancel',

      performance: 'Performance',

      drag_to_sort: 'Drag 以 Sort Order',

      search_any_field: 'Search 任一Field',

      fault: '[EN] 故障',

      efficiency_1: 'Efficiency 1',

      efficiency_2: 'Efficiency 2',

      bypass_pressure: 'Bypass Pressure',

      bypass_valve_count: 'Bypass Valve Count',

      no_active_task_to_cancel: '无活跃Task 可 Cancel',

      detecting: '检测',

      mode: 'Mode',

      temperature_range: 'Temperature Range',

      user_cancelled: 'User cancelled',

      username: 'Username',

      admin_force_cancel: 'Admin override',

      carton_volume_m3: 'carton Volume (m³)',

      carton_width_mm: 'carton 宽 (mm)',

      carton_length_mm: 'carton 长 (mm)',

      carton_height_mm: 'carton 高 (mm)',

      auto_calculated: 'Auto 计算',

      at_least_8_chars: 'At least 8 pcs 字符',

      role: 'Role',

      input_autocomplete: 'Input Auto 补全',

      email: 'Email',

      weight_kg: 'Weight (kg)',

    },

    action: {

      product_name_1: 'Product Name 1',

      product_name_2: 'Product Name 2',

      type: 'Type',

      seal_material: 'Seal Material',

      carton_per_pcs: 'carton/pcs',

      load_failed: 'Load failed:',

      operation_failed: 'Operation failed',

      delete_failed: 'Delete failed',

      restore_failed: 'Resume Failed',

      sort_failed: 'Sort Order Failed',

      brand: 'Brand',

      model: 'Model',

      sort_order: 'Sort Order',

      no_data_click_top_right: '> No data, Click Right 上',

      created: 'Add',

      updated: 'Updated',

      deleted: 'Deleted',

      restored: 'Resumed',

      sort_order_saved: 'Sort Order Saved',

      confirm: 'Confirm',

      resume: 'Resume',

      name: 'Name',

      optional: 'Optional',

    },
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
    success: 'Success',
    failed: 'Failed',
    noData: 'No data',
    noResult: 'No matching results',
    loadFailed: 'Load failed, Please Retry or Contact Admin',
    dictviewcommon: {
      total_drag: 'Total {total} (Active {active}, Soft-deleted {soft}) · Drag to Sort',
    },
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
