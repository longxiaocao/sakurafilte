/**
 * 国际化语言包 - 英文 (en-US)
 * P2.6: English locale for SakuraFilter
 */
export default {
  admin: {
    compareview: {
      error: {
        load_product_failed: 'Load Product Failed',
      },
      placeholder: {
        input_product_id_add: 'Input Product ID Add',
      },
      string: {


        outer_carton: 'Outer Carton',
        outer_carton_pcs: 'Outer Carton/pcs',
        outer_carton_kg: 'Outer Carton 重 (kg)',
        outer_carton_length_mm: 'Outer Carton 长 (mm)',
        outer_carton_width_mm: 'Outer Carton 宽 (mm)',
        outer_carton_height_mm: 'Outer Carton 高 (mm)',
        crossref_vehicle_model: 'CrossRef / Vehicle Model',
        oem_cross_reference: 'OEM Cross-Reference',
        machine_applications: 'Machine Applications',
        load_failed: 'Load failed',
        basic: 'Basic',
        oem_number: 'OEM Number',



        dimensions_mm: 'Dimensions (mm)',


        bypass_lr: 'Bypass LR',
        bypass_hr: 'Bypass HR',



        pressure_resistance_bar: 'Pressure Resistance (bar)',


        media: 'Media',
        media_model: 'Media Model',


      },
      success: {
        remove: 'Remove',
        added_oem: 'Added: {oem}',
      },
      title: {
        move_left: 'Move Left',
        move_right: 'Move Right',
        remove_columns: 'Remove 该columns',
      },
      warning: {
        please_enter_active_product: 'Please enter Active Product ID',
        product_in_compare_list: '该Product in Compare List',
        compare_max_max_products: 'Compare Max {max} Products',
      },
    },
    enginesview: {
      error: {





      },
      label: {



      },
      placeholder: {

        e_g_cummins: 'e.g.: CUMMINS',
        e_g_isb_l: 'e.g.: ISB 4.5 L (可Empty)',
      },
      string: {

      },
      success: {





      },
      title: {
        add_engine: 'Add Engine',
        edit_engine: 'Edit Engine',
      },
      warning: {
        engine_brand_cannot_be_empty: 'Engine Brand cannot be empty',
        engine_brand_length: 'Engine Brand Length 不能超过 200',

      },
    },
    etlview: {
      buttontext: {
        next: 'Next',

        confirm_cancel: 'Confirm Cancel',

        pause: 'Pause',
        no_pause: '不Pause',
        resume: 'Resume',
        no_resume: '不Resume',
      },
      info: {
        description_empty_default: '可补充详细Description (留Empty 用Default)',
        cancel_note: 'Cancel 原因Note',

        task_pause: '无活跃Task 可Pause',
      },
      label: {
        entity: 'Entity',

        file: 'File 路径',
        file_v2: 'File',
        en: '[EN] 大小',
        rows_count: 'rows Count',

        original_json: 'Original JSON',
        timestamp: 'Timestamp',
        error: 'Error',
        en_v2: '[EN] 原因',
        phrase_63454: '读/插/改',
        en_v3: '[EN] 耗时',
        cancel_timestamp: 'Cancel Timestamp',
      },
      placeholder: {
        jsonl_absolute_path: 'JSONL Absolute path',
      },
      string: {
        sse_on_browser_will: 'SSE 连接断On, Browser will Auto 重连',




        task_timeout: 'Task timeout',
        task_execute: 'Task Execute 超时',
        system_shutdown_restart: 'System shutdown/Restart',
        service_close_restart: 'Service Close/Restart',


        cancel_etl_task: 'Cancel ETL Task',


        pause_etl_task: 'Pause ETL Task',

        pause_current_etl_task: 'Pause Current ETL Task?\n\nCurrent batch will Exit gracefully after Complete, checkpoint_id will Write etl_progress_log, the Follow "{resume}" button can be used to 续读 from 该 point.\n\n(Different from "{cancel}" — Cancel will Immediately Terminate and Rollback Current batch)',

        resume_pause_etl_task: 'Resume Pause ETL Task?\\n\\nwill from Recent 一 items paused Record checkpoint_id+1 rows Start 续读, Skip COMMIT batch times.',
        resume_etl_task: 'Resume ETL Task',
        resume_triggered_entity_entity: 'Resume Triggered: entity={entity} checkpoint={checkpoint} (from line {line})',
        resume_triggered_entity_entity_alt: 'Resume Triggered: entity={entity} checkpoint={checkpoint} (from line {line} Continue)',
        copy_staging: 'COPY Staging',
        insert_write_db: 'INSERT Write DB',
        commit_submit: 'COMMIT Submit',
        meili_sync: 'Meili Sync',
        complete: 'Complete',
        on_truncate_clear_xrefs: 'On 启: TRUNCATE 同时 Clear xrefs/apps (首times 全Count 场景); Close: Only 清 products, 保留Off 联Table (单独 Refresh 主Table)',
        auto_recognized_entity_entity: 'Auto-recognized entity={entity}, file: {name}',
        file_filled_name_entity: 'File Filled: {name} (entity need manual select)',
        dropped_total_files_only: 'Dropped {total} files, only first used: {name}',
        on_etl_file: '松On 以填入 ETL File 路径',

        cancel_signal_sent_code: 'Cancel Signal Sent (code: {code}), task will Terminate soon',
      },
      success: {
        dry_run_validation_completed: 'dry-run Validation completed',
        triggered_etl_background_execute: 'Triggered ETL, background Execute',
        phrase_21459: '清除',
      },
      templatetext: {
        immediately_import: 'Immediately Import',
        execute_dry_run: 'Execute dry-run',
        expand_all: 'Expand All',
        collapse_show_front_rows: 'Collapse (只Show Front 10 rows)',
      },
      warning: {

      },
    },
    helpview: {
      string: {
        xlsx_to: '拖拽 XLSX to 此',
        search: 'Search',
        alternative_brand_cross_references: 'Alternative Brand 厂家名 (cross_references.oem_brand), e.g.: Mann, Bosch, Mahle',

        product_name_e_g: 'Product 主Name (e.g.: Oil Filter, Fuel Filter), 影响frontend Product',

        product_name_model_back: 'Product 副Name/Model Back 缀 (e.g.: OF100)',
        category_oil_fuel_air: '5 固定Category: oil / fuel / air / cabin / others, sort_order 决定frontend Sort Order',
        type_type: 'Type (Type)',
        alternative_brand_oem_number: 'Alternative Brand OEM Number (5.27M distinct), 字典化便于 typeahead 联想',
        filter_media_name_model: 'Filter Media Name + Model (2 Field 字典), e.g.: Cellulose / A020',
        media_media: 'Media (Media)',
        machine_brand_model_name: 'Machine Brand + Model + Name, 按 4 大类聚合: Agriculture / Commercial / Construction / others',
        machine_model_machine: 'Machine Model (Machine)',
        engine_brand_model: 'Engine Brand + Model',
        engine_engine: 'Engine (Engine)',
        for_input_oem_number: 'for 什么Input OEM Number Back 无法 Search?',
        check_if_oem_is: 'Check if 该 OEM is in products.oem2 field (Note: not cross_references.oem_brand). Public Page uses oemNoDisplay / oem2, Admin Search uses any field.',
        oem_yes_no_in: '检查该 OEM Yes No in Product Table oem2 Field 里 (注意: 不Yes cross_references.oem_brand). frontend Published 用 oemNoDisplay',
        for_add_product_typeahead: 'for 什么 Add Product 时 typeahead 联想不to 想要 Value?',
        dictionary_is_maintained_in: 'Dictionary is maintained in admin, need to add value in "Dictionary Management" → target dict → Add. typeahead only returns existing values (top 20 by sort_order).',
        dictionary_management: 'Dictionary Management',
        dimensions_search_h_back: 'Dimensions Search (H1 = 100) Back 0 items Result, but 库里有这pcs Product?',
        dimensions_search_default_mm: 'Dimensions Search Default 容差 ±5mm (固定, 不可改), 即 95-105 之间. 如果Product H1 = 110, 不会命. 改用更小 H1 Value or Precise ID Query.',
        etl_trigger_back_in: 'ETL Trigger Back 卡in reading Status?',
        reading_phase_is_streaming: 'reading phase is streaming COPY staging, large files (1M rows) may take 30-60s. If no progress after 5 minutes, check backend log (output/SPIKE-REPORT-*.md) for SQL errors.',
        batch_delete_product: '怎么 Batch Delete Product?',
        in_admin_product_list: 'In admin product list, select multiple rows → top "Batch Discontinue" button. Discontinue = is_discontinued=true, hidden on public page, history preserved. For physical delete, use SQL (carefully).',
        upload_image_back_frontend_sho: 'Upload Image Back frontend 不Show?',
        check_product_ispublished_true: 'Check (1) product isPublished=true (2) slot 1-6 range (3) browser console for OSS pre-signed URL 1h validity. If expired, reload product page.',
        product_ispublished_true_listed: '检查 (1) Product isPublished=true (Listed) (2) slot 1-6 范围 (3) Browser console 看 OSS 预Signature URL 1',
        enter_admin: 'Enter Admin',
        mode_full_load_insert: '+ Mode (full-load / insert-only / upsert), 点',
      },
      title: {
        start: '快速Start',
        en_v4: '[EN] 字典使用规范',
        batch_import: 'Batch Import',
        search_v2: 'Search 容差',
        common: 'Common 问题',
      },
    },
    machinesview: {
      error: {





      },
      label: {



        category: 'Category',

      },
      placeholder: {

        e_g_empty: 'e.g.: 0 451 103 001 (可Empty)',
        e_g_tractor_x: 'e.g.: Tractor X300 (可Empty)',
        select: 'Select 4 大类之一',
      },
      string: {

      },
      success: {





      },
      title: {
        add_machine_model: 'Add Machine Model',
        edit_machine_model: 'Edit Machine Model',
      },
      warning: {
        machine_model_brand_cannot_be: 'Machine Model Brand cannot be empty',
        machine_model_brand_length: 'Machine Model Brand Length 不能超过 200',

      },
    },
    mediasview: {
      error: {





      },
      label: {
        media_name: 'Media Name',
        media_model: 'Media Model',

      },
      placeholder: {
        search_media_name_or: 'Search Media Name or Model',
        e_g_cellulose_synthetic: 'e.g.: Cellulose / Synthetic / Carbon',
        e_g_m_m: 'e.g.: 5μm / 10μm (可Empty)',
      },
      string: {

      },
      success: {





      },
      title: {
        add_media: 'Add Media',
        edit_media: 'Edit Media',
      },
      warning: {
        media_name_cannot_be: 'Media Name cannot be empty',
        media_name_length: 'Media Name Length 不能超过 100',

      },
    },
    oembrandsview: {
      error: {

      },
      label: {


      },
      placeholder: {
        search_brand: 'Search Brand',

      },
      string: {



        add_brand: 'Add Brand',


      },
      success: {





      },
      title: {

        add_oem_brand: 'Add OEM Brand',
        edit_oem_brand: 'Edit OEM Brand',
      },
      warning: {
        brand_cannot_be_empty: 'Brand 名 cannot be empty',
        brand_length: 'Brand 名 Length 不能超过 100',
      },
    },
    oemno3sview: {
      error: {





      },
      label: {

      },
      placeholder: {
        search_oem: 'Search OEM 3',
        e_g: 'e.g.: 11427622448',
      },
      string: {

      },
      success: {





      },
      title: {
        add_oem: 'Add OEM 3',
        edit_oem: 'Edit OEM 3',
      },
      warning: {
        oem_cannot_be_empty: 'OEM 3 cannot be empty',
        oem_length: 'OEM 3 Length 不能超过 200',

      },
    },
    perfview: {
      label: {
        pause_auto_refresh: 'Pause Auto Refresh',
        on_auto_refresh: 'On 启Auto Refresh',
        refresh: 'Refresh 间隔',
      },
      string: {
        p_ms_ms_ms: 'P95 = {ms}ms (≥1000ms Critical)',
        p_ms_ms_ms_v2: 'P95 = {ms}ms (≥500ms Warning)',
        error_rate_pct_critical: 'Error Rate = {pct}% (≥10% Critical)',
        error_rate_pct_warning: 'Error Rate = {pct}% (≥5% Warning)',

        en_v5: '[EN] 就绪',
        downgrade: 'Downgrade',

        refresh_failed: 'Refresh Failed',
      },
      templatetext: {
        pause_v2: '⏸ Pause',
        refresh_v2: 'Refresh …',
        refresh: '↻ Refresh',
        alert: '⚠ 严重Alert',
        warning: '⚠ Warning',

        en: '[EN] 存活',

        en_appsettings_json: '[EN] appsettings.json (兜底)',
        db_load: 'DB ( Load)',
      },
    },
    productformview: {
      error: {
        data_has_been_modified_by: 'Data has been modified by another admin, Please refresh and retry',
        product_already_exists_please: 'Product already exists, Please check the OEM number',



      },
      label: {



        oem_required: 'OEM 2 (Required)',

        remark: 'Remark',




        bypass_valve_lr: 'Bypass Valve LR',
        bypass_valve_hr: 'Bypass Valve HR',

        collapse_pressure_bar: 'Collapse Pressure (bar)',



        master_box_qty: 'Master Box Qty',
        master_carton_kg: 'Master Carton 重 (kg)',
        master_carton_length_mm: 'Master Carton 长 (mm)',
        master_carton_width_mm: 'Master Carton 宽 (mm)',
        master_carton_height_mm: 'Master Carton 高 (mm)',
        master_box_volume_m: 'Master Box Volume (m³)',
      },
      placeholder: {


        brand_input_auto: 'Brand (Input Auto 补全)',
        oem_input_auto: 'OEM 3 (Input Auto 补全)',

        input_auto_name_model: 'Input Auto 补全 (name/model OR 匹配)',


        brand_required: 'Brand (Required)',
        model_required: 'Model (Required)',


        engine_model: 'Engine Model',
      },
      string: {
        by_modify: 'by Modify',
        by_user_modify: 'by 其他User Modify',
        slot_slot_uploaded: 'Slot {slot} Uploaded',
        slot_slot_deleted: 'Slot {slot} Deleted',
        edit_product_id: 'Edit Product #{id}',
        cross_reference_count: '② Cross-Reference ({count})',
        machine_applications_count: '⑥ Machine Applications ({count})',
      },
      success: {
        saved: 'Saved',
        created: 'Created',
      },
      templatetext: {
        add_product: 'Add Product',
      },
      title: {
        basic_info: 'Basic Info',
        dimensions_mm: '③ Dimensions (mm)',

        image: '⑦ Image (1-6 槽位)',
      },
      warning: {
        please_first_save_product_then: 'Please first Save Product then Upload Image',
      },
    },
    productname1sview: {
      error: {

      },
      label: {


      },
      placeholder: {
        search_product_name: 'Search Product Name 1',
        e_g_oil_filter: 'e.g.: OIL FILTER',
      },
      string: {





      },
      success: {





      },
      title: {

        add_product: 'Add Product 名 1',
        edit_product: 'Edit Product 名 1',
      },
      warning: {
        product_name_cannot_be: 'Product Name 1 cannot be empty',
        product_name_length: 'Product Name 1 Length 不能超过 200',
      },
    },
    productname2sview: {
      error: {





      },
      label: {

      },
      placeholder: {
        search_product_name: 'Search Product Name 2',
        e_g_spin_on: 'e.g.: SPIN-ON',
      },
      string: {

      },
      success: {





      },
      title: {
        add_product: 'Add Product 名 2',
        edit_product: 'Edit Product 名 2',
      },
      warning: {
        product_name_cannot_be: 'Product Name 2 cannot be empty',
        product_name_length: 'Product Name 2 Length 不能超过 200',

      },
    },
    productsview: {
      aria: {
        oem_search: 'OEM 2 Search',
        mr_search: 'MR.1 Search',
        product_name_search: 'Product Name Search',
        filter_by_type: 'Filter by Type',
        oem_batch_search: 'OEM 3 Batch Search',
      },
      label: {



        discontinued: 'Discontinued',
        update: 'Update',
        action: 'Action',

        field: 'Field',
        value: '新Value',
      },
      placeholder: {

        oem_batch_count: 'OEM 3 batch Count',



        efficiency: 'Efficiency',




      },
      string: {
        all_columns: 'All columns',
        columns: '核心columns',
      },
      success: {
        discontinued_v2: 'Discontinued',

      },
      title: {
        filter: '高级Filter',
        en_v6: '[EN] 变更历史',
      },
      warning: {

        please_select_pcs_product: 'Please select 2-6 pcs Product',
        at_most_compare_pcs: 'At most Compare 6 pcs',
      },
    },
    typesview: {
      error: {





      },
      label: {

      },
      placeholder: {
        search_type: 'Search Type',
        e_g_oil_fuel: 'e.g.: oil / fuel / air / cabin / others',
      },
      string: {


      },
      success: {




        sort_order_saved_frontend: 'Sort Order Saved, frontend Product P2.3 Immediately take effect',
      },
      title: {
        add_type: 'Add Type',
        edit_type: 'Edit Type',
      },
      warning: {
        type_cannot_be_empty: 'Type cannot be empty',
        type_length: 'Type Length 不能超过 50',
      },
    },
    usersview: {
      label: {
        user_list: 'User List',
        login_audit: 'Login Audit',

        password: 'Password',



        enable_status: 'Enable Status',
        password_v2: '新Password',
      },
      placeholder: {
        login_username: 'Login Username',


      },
      string: {

        password_of_user_has: 'Password of {user} has been Reset',
        admin_admin: 'Admin (admin)',
        action_operator: 'Action 员 (operator)',
        read_only_viewer: 'Read-only (viewer)',
      },
      success: {
        user_created: 'User Created',
        user_updated: 'User Updated',

        logout: 'Logout',
      },
      title: {
        add_user: 'Add User',
        edit_user_user: 'Edit User: {user}',
        reset_password_user: 'Reset Password: {user}',
      },
      warning: {
        password_at_least_pcs: 'Password At least 8 pcs 字符',
        password_at_least_pcs_v2: '新Password At least 8 pcs 字符',
        username_cannot_be_empty: 'Username cannot be empty',
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

      ready: 'Ready',

      efficiency_1: 'Efficiency 1',

      efficiency_2: 'Efficiency 2',

      bypass_pressure: 'Bypass Pressure',

      bypass_valve_count: 'Bypass Valve Count',

      downgrade: 'Downgrade',

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
      error_001: 'Server is busy, please try again later (Error code: ${status})',
      error_002: 'Network connection failed, please check the network',
      error_003: 'Please enter password',
      error_004: 'Please enter your current password',
      error_005: 'Please enter username',
      info_001: 'OEM Lookup',
      info_002: 'About to trigger ${entity} ETL (${mode}${dryRun ? \', dry run\' : \'\'}), continue?',
      info_003: 'Search message / type / tags…',
      info_004: 'Search path / method / summary…',
      info_005: 'Paste OEM numbers, one per line (tab/line break/comma/semicolon delimited)&#10;Example:&#10;OEN-123&#10;AB/CD/456&#10;Filter 1142',
      info_006: 'Network exception: ${err.message || \'please try again later\'}',
      info_007: 'Request rate limit exceeded, please try again in ${retryAfter || 60}s',
      info_008: 'Try entering 045090',
      info_009: 'Enter product ID to add',
      success_001: 'Added: ${data.items[0].oemNoDisplay}',
      success_002: 'Pause signal sent (code: ${code}), task will terminate soon',
      warn_001: 'Re-enter the new password',
      warn_002: 'Up to 500 OEMs, currently ${oems.length} entered',
      warn_003: 'Compare up to ${MAX_COMPARE} products',
      warn_004: 'Clear confirmation',
      warn_005: 'Confirm discontinued product ',
      warn_006: 'Confirm delete ',
      warn_007: 'Confirm delete brand ',
      warn_008: 'Confirm delete user ',
      warn_009: 'Confirm',
      warn_010: '8 characters or more',
    
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

      ready: 'Ready',

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
    // P-Admin-UX: Common feedback messages (ElMessage.success/warning/error/info)
    //   Prefixed by severity: error/success/info/warn
    //   vue-i18n falls back to key string when missing, so must declare explicitly
    feedback: {
      // ----- Error -----
      error_002: 'Export failed',
      error_003: 'You can compare up to 6 products',
      error_008: 'Copy failed',
      error_009: 'Clear failed',
      error_018: 'Clear failed',
      error_022: 'Failed to load API docs, please check backend Swagger',
      error_023: 'Failed to load local API docs',
      error_029: 'Request failed, please check your network',
      error_045: 'Please enter a search keyword',
      error_048: 'Please add products to compare first',
      // ----- Success -----
      success_002: 'Copied to clipboard',
      success_010: 'Password changed',
      success_012: 'Loaded cached API docs',
      success_014: 'Error logs reloaded',
      success_015: 'All error logs cleared',
      success_016: 'Already in compare list, navigating',
      success_019: 'Logged out',
      // ----- Info -----
      info_004: 'OEM cannot be empty',
      info_005: 'Permission denied, redirected to product management',
      info_017: 'Added to compare',
      info_024: 'No product found for this OEM',
      info_030: 'Network error, please check connection',
      info_041: 'Please enter product ID',
      info_042: 'Please sign in first',
      info_043: 'Session expired, please sign in again',
      // ----- Warning -----
      warn_040: 'Compare list is full (6/6), please remove first'
    },
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
    singleTitle: 'Single Search',
    // Entry to public search page (8-field typeahead + table + compare, for customers/public)
    advancedSearch: 'Advanced Search'
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
    timestamp: 'Time',
  },
  a11y: {
    skipToContent: 'Skip to main content',
  },
}
