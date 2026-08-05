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
        outer_carton_kg: 'Outer Carton Weight (kg)',
        outer_carton_length_mm: 'Outer Carton Length (mm)',
        outer_carton_width_mm: 'Outer Carton Width (mm)',
        outer_carton_height_mm: 'Outer Carton Height (mm)',
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
      page_title: 'ETL Trigger & Monitor',
      guide_title: 'How to use',
      guide_step1: '1. Select data entity: Products / OEM Cross-References / Machine Applications',
      guide_step2: '2. Select import mode: Full reload (truncate & re-import) / Insert only (skip existing) / Upsert (overwrite existing)',
      guide_step3: '3. Enter the data file path (absolute path accessible inside container, e.g. /tmp/etl/products.jsonl), or drag & drop an XLSX file',
      guide_step4: '4. Click "Trigger ETL" to run in background; you may click "Run dry-run" to validate only',
      guide_step5: '5. Check "Pipeline" and "Recent errors" for progress and failure reasons',
      entity: {
        products: 'Products',
        xrefs: 'OEM Cross-References',
        apps: 'Machine Applications'
      },
      mode: {
        full_load: 'Full reload (truncate & re-import)',
        insert_only: 'Insert only (skip existing)',
        upsert: 'Upsert (overwrite existing)'
      },
      dry_run_check: 'Dry-run (validate only, no import)',
      section: {
        pipeline: 'Data Pipeline',
        trigger: 'Manual ETL Trigger',
        alert_status: 'Alert Status',
        last_finished: 'Last Finished Result',
        dry_run: 'Recent dry-run Validation',
        recent_errors: 'Recent Errors (max 10)',
        audit: 'Cancel Audit (aggregate by reason_code)'
      ,reindex_confirm: 'Full Rebuild', total_cancelled: 'Total Cancelled', no_cancelled_records: 'No cancelled records'
      },
      pipeline: {
        stage_read: 'Read',
        stage_staging: 'Staging',
        stage_insert: 'Insert',
        stage_commit: 'Commit',
        stage_meili: 'Meili',
        stage_done: 'Done',
        stage_idle: 'Idle',
        status_running: 'Running',
        status_completed: 'Completed',
        status_failed: 'Failed',
        status_paused: 'Paused',
        status_cancelled: 'Cancelled',
        status_idle: 'Idle',
        elapsed_label: 'Elapsed',
        errors_label: 'Errors'
      },
      kpi: {
        trigger_24h: '24h Triggers',
        success_24h: '24h Success',
        failed_24h: '24h Failed',
        avg_duration: '24h Avg Duration',
        last_24h: 'Last 24 hours',
        success_rate: 'Success rate {rate}%',
        need_attention: 'Needs attention',
        all_ok: 'All OK',
        completed_only: 'Completed tasks only'
      },
      alert: {
        p2_tag: 'P2 Pending',
        title: 'Alert System',
        description: 'Alert system (DingTalk / WeChat / Generic Webhook / WeChat MP) is now active.',
        planned_types: 'Alert types',
        planned_channels: 'Channels',
        type_etl: 'ETL Task',
        type_perf: 'Performance',
        type_security: 'Security',
        type_access: 'Access',
        type_resource: 'Resource',
        channel_dingtalk: 'DingTalk',
        channel_wechat: 'WeChat Work',
        channel_webhook: 'Generic Webhook',
        view_design_btn: 'View Alert Design Doc',
        // P2-1 real alert KPIs
        '7d_failed': '7d Failed',
        '7d_p0': '7d P0',
        '7d_sent': '7d Sent',
        latest: 'Latest',
        no_history: 'No alert history',
        view_all_btn: 'View Alert Center'
      },
      audit: {
        observable_tag: 'Operational',
        recent_20_cancelled: 'Recent 20 cancelled records',
        reason_code: 'Reason Code',
        legacy: 'Legacy'
      },
      dry_run: {
        samples_count: '{count} samples',
        samples_preview: 'Sample preview (top {count} JSON rows)'
      },
      buttontext: {
        next: 'Next',

        confirm_cancel: 'Confirm Cancel',

        pause: 'Pause',
        no_pause: "Don't Pause",
        resume: 'Resume',
        no_resume: "Don't Resume",
      },
      info: {
        description_empty_default: 'Optional detailed description (empty = use default)',
        cancel_note: 'Cancel Reason Note',

        task_pause: 'No active task to pause',
      },
      label: {
        entity: 'Entity',

        file: 'File Path',
        file_v2: 'File',
        en: '[EN] 大小',
        rows_count: 'Rows Count',

        original_json: 'Original JSON',
        timestamp: 'Timestamp',
        error: 'Error',
        en_v2: 'Reason',
        phrase_63454: 'Read/Insert/Update',
        en_v3: 'Duration',
        cancel_timestamp: 'Cancel Timestamp',
      },
      placeholder: {
        jsonl_absolute_path: 'JSONL Absolute Path',
      },
      string: {
        sse_on_browser_will: 'SSE disconnected, browser will auto-reconnect',




        task_timeout: 'Task timeout',
        task_execute: 'Task execution timeout',
        system_shutdown_restart: 'System shutdown/restart',
        service_close_restart: 'Service close/restart',


        cancel_etl_task: 'Cancel ETL Task',


        pause_etl_task: 'Pause ETL Task',

        pause_current_etl_task: 'Pause current ETL task?\n\nCurrent batch will exit gracefully after complete, checkpoint_id will be written to etl_progress_log, the following "{resume}" button can be used to continue from that point.\n\n(Different from "{cancel}" — cancel will immediately terminate and rollback current batch)',

        resume_pause_etl_task: 'Resume the paused ETL task?\n\nWill continue from the latest paused record\'s checkpoint_id+1 row, skipping committed batches.',
        resume_etl_task: 'Resume ETL Task',
        resume_triggered_entity_entity: 'Resume triggered: entity={entity} checkpoint={checkpoint} (from line {line})',
        resume_triggered_entity_entity_alt: 'Resume triggered: entity={entity} checkpoint={checkpoint} (continue from line {line})',
        copy_staging: 'COPY Staging',
        insert_write_db: 'INSERT Write DB',
        commit_submit: 'COMMIT Submit',
        meili_sync: 'Meili Sync',
        complete: 'Complete',
        on_truncate_clear_xrefs: 'On: TRUNCATE clears xrefs/apps too (initial full load); Off: only clear products, keep related tables (refresh main table separately)',
        auto_recognized_entity_entity: 'Auto-recognized entity={entity}, file: {name}',
        file_filled_name_entity: 'File filled: {name} (entity needs manual select)',
        dropped_total_files_only: 'Dropped {total} files, only first used: {name}',
        on_etl_file: 'Drop here to fill ETL file path',

        cancel_signal_sent_code: 'Cancel signal sent (code: {code}), task will terminate soon',
        // V24-F103 i18n residue fix: full rebuild Meilisearch index text
        full_rebuild: 'Full Rebuild',
        full_rebuild_tip: 'Clears all Meilisearch documents and re-syncs from PostgreSQL. Mutually exclusive with ETL tasks.',
        full_rebuild_alert_title: 'Execution will clear all Meilisearch documents and re-sync. Search will be briefly unavailable during this period.',
        full_rebuild_alert_desc: 'Use cases: force rebuild after index schema change / data drift repair / schema field update',
      },
      success: {
        dry_run_validation_completed: 'dry-run validation completed',
        triggered_etl_background_execute: 'Triggered ETL, background execute',
        phrase_21459: 'Cleared',
      },
      templatetext: {
        immediately_import: 'Immediately Import',
        execute_dry_run: 'Execute dry-run',
        expand_all: 'Expand All {count} rows',
        collapse_show_front_rows: 'Collapse (show top 10 rows)',
        cancel_task: 'Cancel Task',
      },
      warning: {

      },
    },
    alertsview: {
      page_title: 'Alert Center',
      btn: {
        test: 'Test Alert',
        rules: 'Alert Rules'
      },
      kpi: {
        total_7d: '7d Total',
        sent: 'Sent',
        failed: 'Failed',
        suppressed: 'Suppressed',
        p0: 'P0 Critical',
        p1: 'P1 Data',
        last_7d: 'Last 7 days',
        send_success: 'Send success',
        send_failed: 'Send failed',
        suppressed_in_window: 'In suppress window',
        severity_p0: 'Severity P0',
        severity_p1: 'Severity P1'
      },
      filter: {
        type: 'Type',
        severity: 'Severity',
        status: 'Status',
        all: 'All'
      },
      table: {
        title: 'Alert History',
        records: 'records',
        severity: 'Severity',
        type: 'Type',
        title_col: 'Title',
        channel: 'Channel',
        status: 'Status',
        sent_at: 'Sent At',
        actions: 'Actions',
        detail: 'Detail'
      },
      detail: {
        title: 'Alert Detail',
        id: 'ID',
        severity_type: 'Severity / Type',
        title_col: 'Title',
        channel_status: 'Channel / Status',
        sent_at: 'Sent At',
        recipients: 'Recipients',
        error: 'Error',
        content: 'Full Payload',
        response: 'Channel Response'
      },
      rules: {
        title: 'Alert Rules',
        empty: 'No rules (insert into alert_rules table or call backend API)',
        type: 'Type',
        severity: 'Severity',
        channels: 'Channels',
        enabled: 'Enabled'
      },
      test: {
        triggered_by_user: 'Manually triggered by current user'
      }
    },
    helpview: {
      string: {
        xlsx_to: 'Drag XLSX here',
        search: 'Search',
        alternative_brand_cross_references: 'Alternative brand manufacturer (cross_references.oem_brand), e.g.: Mann, Bosch, Mahle',

        product_name_e_g: 'Product main name (e.g.: Oil Filter, Fuel Filter), affects frontend product page',

        product_name_model_back: 'Product sub-name/model suffix (e.g.: OF100)',
        category_oil_fuel_air: '5 fixed categories: oil / fuel / air / cabin / others, sort_order determines frontend sort order',
        type_type: 'Type',
        alternative_brand_oem_number: 'Alternative brand OEM number (5.27M distinct), dictionary for typeahead',
        filter_media_name_model: 'Filter media name + model (2-field dict), e.g.: Cellulose / A020',
        media_media: 'Media',
        machine_brand_model_name: 'Machine brand + model + name, aggregated by 4 categories: Agriculture / Commercial / Construction / others',
        machine_model_machine: 'Machine Model',
        engine_brand_model: 'Engine Brand + Model',
        engine_engine: 'Engine',
        for_input_oem_number: 'Why does inputting OEM number return no search results?',
        check_if_oem_is: 'Check if the OEM is in products.oem2 field (Note: not cross_references.oem_brand). Public page uses oemNoDisplay / oem2, Admin search uses any field.',
        oem_yes_no_in: 'Check if the OEM is in product table oem2 field (Note: not cross_references.oem_brand). Frontend published uses oemNoDisplay',
        for_add_product_typeahead: 'Why does typeahead on Add Product not suggest the desired value?',
        dictionary_is_maintained_in: 'Dictionary is maintained in admin, need to add value in "Dictionary Management" → target dict → Add. typeahead only returns existing values (top 20 by sort_order).',
        dictionary_management: 'Dictionary Management',
        dimensions_search_h_back: 'Dimensions search (H1 = 100) returns 0 results, but the product exists in DB?',
        dimensions_search_default_mm: 'Dimensions search default tolerance ±5mm (fixed, not configurable), i.e. 95-105. If product H1 = 110, no match. Use smaller H1 value or precise ID query.',
        etl_trigger_back_in: 'ETL trigger stuck in reading status?',
        reading_phase_is_streaming: 'reading phase is streaming COPY staging, large files (1M rows) may take 30-60s. If no progress after 5 minutes, check backend log (output/SPIKE-REPORT-*.md) for SQL errors.',
        batch_delete_product: 'How to batch delete products?',
        in_admin_product_list: 'In admin product list, select multiple rows → top "Batch Discontinue" button. Discontinue = is_discontinued=true, hidden on public page, history preserved. For physical delete, use SQL (carefully).',
        upload_image_back_frontend_sho: 'Uploaded image not showing on frontend?',
        check_product_ispublished_true: 'Check (1) product isPublished=true (2) slot 1-6 range (3) browser console for OSS pre-signed URL 1h validity. If expired, reload product page.',
        product_ispublished_true_listed: 'Check (1) product isPublished=true (listed) (2) slot 1-6 range (3) browser console for OSS pre-signed URL 1h validity',
        enter_admin: 'Enter Admin',
        mode_full_load_insert: '+ Mode (full-load / insert-only / upsert), click',
        // V24-F103 i18n residue fix: HelpView static doc content i18n
        page_title: 'Admin Operation Guide',
        page_subtitle: '5 modules: Quick Start / Dictionary Specs / Bulk Import / Search Tolerance / FAQ',
        quick_start_title: '1. Quick Start (5 steps)',
        quick_start_step1: "Click \"Enter Admin\" at top-right, enter credentials (admin/Admin{'@'}2026 or operator/Operator{'@'}2026)",
        quick_start_step2: 'Dictionary Management → seed 8 dictionaries (first deploy): run spike-test/_seed_dict_*.py (6 scripts)',
        quick_start_step3: 'ETL Trigger → select products.xlsx / xrefs.xlsx / apps.xlsx, recommend "full-load" mode',
        quick_start_step4: 'Product Management → query by 8 fields / OEM / bulk paste, click product to enter detail',
        quick_start_step5: 'Product detail page supports uploading 6 images (slot 1-6) + editing 7 partition fields (admin product form)',
        dict_norms_title: '2. Dictionary Specs (8 items)',
        dict_norms_desc: 'Dictionary = admin-maintained standard value set. Public typeahead / admin form / public search all read from dictionary, ensuring site-wide consistency.',
        col_dict: 'Dict',
        col_field: 'Ref Field',
        col_desc: 'Description',
        col_unit: 'Unit',
        col_example: 'Example',
        dict_drag_tip: '💡 Drag-sort: the ≡ handle on each row of dictionary admin page, sort_order persisted, public display sorted by sort_order asc.',
        batch_import_title: '3. Bulk Import Flow (XLSX drag-drop)',
        batch_import_step1: 'Prepare Excel: products / xrefs / machine_applications tables (column names see ETL Trigger page)',
        batch_import_step2: 'ETL Trigger page → drag XLSX file → auto-detect entity + mode (recommend full-load, insert-only for additions)',
        batch_import_step3: 'Progress 5 stages: reading → staging → inserting → committing → meili-sync, any failure can pause/resume',
        batch_import_step4: 'On completion, a row is written to etl_progress_log (with read/stage/inserted/skipped/missing_oem/error counts)',
        batch_import_step5: 'Admin product management "Search" to verify imported data is queryable',
        batch_import_perf: '⚠ Performance: 1M products full-load ~2-3 min, 5M xrefs ~5-8 min, 1M apps ~2 min (PG local test data)',
        search_tolerance_title: '4. Search Tolerance (±5mm fixed)',
        search_tolerance_desc: 'Dimension fields (H1-H4 / D1-D4) search default ±5mm tolerance, i.e. H1=100 matches products with H1∈[95,105]. Backend AdminProductService hardcodes tolerance=5, frontend does not expose toggle.',
        search_tolerance_combo: 'Multi-field combinations use AND (narrowing), single field match returns (public search supports all 8 fields).',
        field_help_title: 'Field Help (top 10 commonly used)',
        field_help_tip: 'Full field help: hover the ? icon next to each field in admin product form.',
        faq_title: '5. Frequently Asked Questions (FAQ)',
        footer: 'SakuraFilter Admin · Help Docs',
      },
      title: {
        start: 'Quick Start',
        en_v4: 'Dictionary Specs',
        batch_import: 'Batch Import',
        search_v2: 'Search Tolerance',
        common: 'FAQ',
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
      // P1 Task 3: tree + batch bind button labels
      btn: {
        view_tree: 'View 3-Level Tree',
        batch_bind_mr1: 'Batch Bind MR.1',
      },
      // P1 Task 3: tree dialog labels
      tree_dialog: {
        title: 'Machine 3-Level Tree (Category → Brand → Model)',
        node_count: '{count} machine models',
      },
      // P1 Task 3: batch bind dialog labels
      bind_dialog: {
        title: 'Batch Bind MR.1 to Machine',
        label_machine: 'Machine',
        label_mr1_list: 'MR.1 List (one per line)',
        label_replace: 'Bind Mode',
        replace_append: 'Append (keep existing bindings)',
        replace_replace: 'Replace (clear existing bindings)',
        placeholder_mr1: 'Paste MR.1 numbers, one per line',
        submit: 'Submit Bind',
        result_title: 'Bind Result',
        result_bound: 'Bound',
        result_skipped: 'Skipped (already bound)',
        result_removed: 'Removed',
        result_not_found: 'MR.1 not found ({count})',
        success: 'Bind complete',
        partial: 'Partial success',
        error_machine_required: 'Please select a machine',
        error_mr1_empty: 'MR.1 list cannot be empty',
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
      export_csv: 'Export CSV',
      import_csv: 'Import CSV',
      import_done: 'Import done',
      export_failed: 'Export failed, retry later',
      import_failed: 'Import failed, check format (each line: brand[,sortOrder[,deleted]])',
      import_partial_fail: 'Some rows failed',
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
        master_carton_kg: 'Master Carton Weight (kg)',
        master_carton_length_mm: 'Master Carton Length (mm)',
        master_carton_width_mm: 'Master Carton Width (mm)',
        master_carton_height_mm: 'Master Carton Height (mm)',
        master_box_volume_m: 'Master Box Volume (m³)',
      },
      placeholder: {


        brand_input_auto: 'Brand (Input Auto Complete)',
        oem_input_auto: 'OEM 3 (Input Auto Complete)',

        input_auto_name_model: 'Input Auto Complete (name/model OR match)',


        brand_required: 'Brand (Required)',
        model_required: 'Model (Required)',


        engine_model: 'Engine Model',
      },
      string: {
        by_modify: 'by Modify',
        by_user_modify: 'by other user',
        slot_slot_uploaded: 'Slot {slot} Uploaded',
        slot_slot_deleted: 'Slot {slot} Deleted',
        edit_product_id: 'Edit Product #{id}',
        cross_reference_count: '② Cross-Reference ({count})',
        machine_applications_count: '⑥ Machine Applications ({count})',
        add_xref: '+ Add Cross-Reference',
        add_machine_app: '+ Add Machine Application',
        back_to_list: 'Back to List',
        load_failed_subtitle: 'Failed to load product data. Please retry or go back to list.',
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

        image: '⑦ Image (1-6 slots)',
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
        columns: 'Core Columns',
      },
      success: {
        discontinued_v2: 'Discontinued',

      },
      title: {
        filter: 'Advanced Filter',
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
    // V24-F72: 补充 errorview aria key (AdminErrorView 用)
    errorview: {
      aria: {
        trigger_test_error: 'Trigger test error',
      },
    },
    // 2026-08-01: AdminXrefReorderView i18n 化 (V24-F86 页面补充)
    xrefreorder: {
      page_title: 'OEM Whitelist Management',
      page_subtitle: 'Drag to adjust OEM 3 display order within the whitelist (smaller = higher priority, bidding-like ranking) · Auto-save · Refresh & retry on conflict · Whitelisted products only',
      brand_label: 'Brands ({count})',
      add_brand: '+ Add',
      brand_whitelist_count: 'Whitelist {count} items · brand sort: {sortOrder}',
      no_brand_data: 'No brand data',
      select_brand_placeholder: 'Select a brand',
      total_info: '({total} items, page {page}/{totalPages})',
      search_placeholder: 'Search OEM 3 in whitelist',
      add_to_whitelist: 'Add to whitelist',
      save_order: 'Save order',
      mr1_label: 'MR.1: {mr1}',
      unpublished: 'Unpublished',
      sort_label: 'sort: {sortOrder}',
      edit: 'Edit',
      remove_from_whitelist: 'Remove from whitelist',
      empty_no_whitelist: 'No whitelist maintained for this brand yet. Click "Add to whitelist" to pick products to feature',
      empty_select_brand: 'Select a brand on the left',
      dialog_add_title: 'Add to whitelist',
      dialog_edit_title: 'Edit OEM 3',
      field_current_brand: 'Brand',
      field_product: 'Linked product',
      product_placeholder: 'Search product by MR.1 / name under this brand',
      field_oem_no3: 'OEM 3 No.',
      field_oem2: 'OEM 2',
      field_machine_type: 'Machine type',
      field_is_published: 'Published',
      dialog_add_hint: 'Note: the product joins the whitelist on submit (sort_order = current max + 1), appended to the end; drag to reorder',
      cancel: 'Cancel',
      save: 'Save',
      add_brand_dialog_title: 'Add brand',
      field_brand_name: 'Brand name',
      brand_name_placeholder: 'Enter brand name (e.g. BOSCH, DONALDSON)',
      add_brand_hint: 'Note: the brand joins the dictionary on submit, appended to the end; you can then "Add to whitelist" under it',
      err_load_brands: 'Failed to load brand list',
      err_load_oem_list: 'Failed to load OEM 3 list',
      success_saved_order: 'Saved order of {count} OEM 3 items',
      warn_item_deleted: 'OEM 3 {oemNo3} was deleted by another user. List refreshed, please drag again',
      conflict_message: 'OEM 3 order was modified by another user. Auto-retry failed, please refresh and retry manually. Refresh now?',
      conflict_title: 'Order conflict',
      refresh: 'Refresh',
      err_save_order: 'Failed to save order',
      err_load_detail: 'Failed to load detail',
      err_select_product: 'Please select a linked product',
      err_oem_brand_required: 'oemBrand is required',
      err_oem_no3_required: 'oemNo3 is required',
      success_added: 'Added to whitelist',
      success_edited: 'Saved',
      err_conflict: 'Conflict, please refresh and retry',
      err_save_failed: 'Save failed',
      confirm_remove_message: 'Remove OEM 3 "{oemNo3}" from whitelist? (The product itself is not deleted, it just stops being featured)',
      remove: 'Remove',
      success_removed: 'Removed from whitelist',
      err_remove_failed: 'Remove failed',
      err_brand_name_required: 'Please enter a brand name',
      success_brand_restored: "Brand '{brand}' restored",
      success_brand_added: "Brand '{brand}' added",
      err_brand_exists: 'Brand already exists',
      err_add_brand_failed: 'Failed to add brand',
    },
    // 2026-08-01: AdminApiDocsView i18n 化 (批次 6d 页面补充)
    apidocs: {
      page_title: 'API Docs',
      subtitle_batch: 'Batch 6d — OpenAPI 3.0 Browser',
      subtitle_source: '· Live from /swagger/v1/swagger.json',
      loading: 'Loading…',
      refresh: '↻ Refresh',
      stat_modules: 'Modules',
      stat_endpoints: 'Endpoints',
      stat_schemas: 'Data models',
      filter_module_aria: 'Filter by module',
      filter_all_modules: 'All modules',
      search_placeholder: 'Search path / method / summary…',
      search_aria: 'Search endpoints',
      showing_count: 'Showing {shown} / {total}',
      retrying: 'Retrying…',
      retry: 'Retry',
      endpoint_count: '({count} endpoints)',
      copy_curl_aria: 'Copy cURL for {method} {path}',
      no_matching_endpoints: 'No matching endpoints',
      err_swagger_unavailable: 'Swagger unavailable, falling back',
      err_swagger_and_local_unavailable: 'Swagger unavailable and local openapi.json is also unreachable',
      err_swagger_and_openapi_failed: 'Both Swagger and openapi.json failed to load',
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

      carton_volume_m3: 'Carton Volume (m³)',

      carton_width_mm: 'Carton Width (mm)',

      carton_length_mm: 'Carton Length (mm)',

      carton_height_mm: 'Carton Height (mm)',

      auto_calculated: 'Auto calculated',

      at_least_8_chars: 'At least 8 characters',

      role: 'Role',

      input_autocomplete: 'Input Auto Complete',

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

      // V24-F72: 补充 common.action 下缺失的通用操作 key
      search: 'Search',
      reset: 'Reset',
      refresh: 'Refresh',
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
    readonly: 'Read-only',
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
    // V24-F103 i18n residue fix: aria-label a11y text (AppHeader + DictManagerLayout)
    aria: {
      openNav: 'Open navigation menu',
      mainNav: 'Main navigation',
      mobileNav: 'Mobile navigation menu',
      expandMore: 'Expand more functions menu',
      themeToggle: 'Toggle theme',
      switchLang: 'Switch language, current {lang}',
      userMenu: 'User menu: {username}, role {role}',
      searchBox: 'Global search box, shows suggestions while typing, press Enter to open aggregate search',
      searchPlaceholder: 'Search product / OEM / model',
      enterAdminLogin: 'Enter admin login',
      switchToLight: 'Switch to light',
      switchToDark: 'Switch to dark'
    },
    dictviewcommon: {
      total_drag: 'Total {total} (Active {active}, Soft-deleted {soft}) · Drag to Sort',
    },
    // V24-F43 (spec Task 0.5.6/F3-4): backend errorCode → English friendly message mapping
    //   WHY: http.ts interceptor fallback chain i18n.global.t('common.error.' + errorCode) looks up this table
    //   Unmatched errorCode falls back to ERROR_CODE_MAP[status] → data.title → Request failed (status)
    error: {
      // ===== Legacy ERR_ prefix error codes (10) =====
      ERR_VALIDATION_FAILED: 'Request validation failed',
      ERR_NOT_FOUND: 'Requested resource not found',
      ERR_CONFLICT: 'Resource already exists or conflict',
      ERR_FORBIDDEN: 'No permission to perform this operation',
      ERR_CANCELLED: 'Request cancelled',
      ERR_INTERNAL: 'Internal server error, please retry later',
      ERR_DB_CONFLICT: 'Data conflict (possibly modified by another user), please refresh and retry',
      ERR_DB_CONSTRAINT: 'Data constraint failed (foreign key or not-null violation)',
      ERR_DB_TIMEOUT: 'Database busy, please retry later',
      ERR_AUTH_FAILED: 'Invalid username or password',
      // ===== V2 error codes (15, no ERR_ prefix) =====
      MR1_REQUIRED: 'MR.1 number is required',
      MR1_FORMAT_INVALID: 'MR.1 number format is invalid',
      MR1_ALREADY_EXISTS: 'MR.1 number already exists',
      OEM3_ALREADY_EXISTS: 'OEM 3 number already exists',
      MACHINE_TYPE_INVALID: 'Machine type is invalid',
      XREF_CONFLICT: 'Cross-reference conflict (possibly modified by another user), please refresh and retry',
      SEARCH_PAGE_TOO_DEEP: 'Search page too deep, please search again',
      CURSOR_INVALID: 'Pagination cursor invalid, reset to page 1',
      CURSOR_EXPIRED: 'Pagination cursor expired, reset to page 1',
      IMAGE_ROLE_SLOT_MISMATCH: 'Image role and slot mismatch',
      IMAGE_DETAIL_SLOT_INVALID: 'Image detail slot invalid (must be 1-6)',
      IMAGE_PRIMARY_DUPLICATE: 'Primary image already exists (only 1 primary image per product)',
      IMAGE_DETAIL_SLOT_DUPLICATE: 'Image detail slot duplicate',
      MR1_NOT_FOUND: 'MR.1 number not found',
      OEM3_NOT_FOUND: 'OEM 3 number not found',
      INVALID_FILE_TYPE: 'Invalid file type',
      FILE_TOO_LARGE: 'File too large',
      EMPTY_FILE: 'Empty file',
      MR1_EMPTY: 'mr_1 is empty',
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
    opsCenter: 'Ops Center',
    // 🔧 fix(审查): 运维中心 el-tabs label i18n
    opsview: {
      tab: { etl: 'ETL Trigger & Monitor', perf: 'Performance', errors: 'Errors', api: 'API Docs', storage: 'Storage Config' }
    },
    storage: {
      provider: 'Storage Provider',
      provider_tip: 'Save then restart container to apply; the image proxy endpoint works with all providers',
      endpoint: 'Endpoint',
      access_key: 'Access Key',
      secret_key: 'Secret Key',
      access_key_id: 'AccessKey ID',
      access_key_secret: 'AccessKey Secret',
      bucket: 'Bucket Name',
      public_endpoint: 'Public Endpoint (browser URL)',
      cdn_endpoint: 'CDN Domain (optional)',
      test: 'Test Connection',
      save: 'Save Config',
      test_ok: 'Connection test passed',
      test_failed: 'Connection test failed',
      saved: 'Saved, restart container to apply',
      save_failed: 'Save failed, retry later',
      load_failed: 'Failed to load config',
      restart_tip: 'Saved config applies after container restart (storage client is singleton). The test button verifies credentials/connectivity (upload/read/delete probe).',
    },
    help: 'Help',
    enterAdmin: 'Enter Admin',
    exitAdmin: 'Exit Admin',
    // V24-F103 i18n residue fix: AppHeader nav buttons + dict dropdown + drawer
    more: 'More',
    about: 'About us',
    news: 'News',
    contact: 'Contact us',
    product: 'Product',
    advSearch: 'Advanced Search',
    xrefReorder: 'OEM Whitelist',
    advCompare: 'Advanced Compare',
    errors: 'Errors',
    api: 'API',
    siteContent: 'Site Content',
    dictGroup: 'Dictionary',
    dictItems: {
      oemBrand: 'OEM Brand',
      productName1: 'Product Name 1',
      productName2: 'Product Name 2',
      type: 'Type',
      oem3: 'OEM 3',
      media: 'Media',
      machine: 'Machine',
      engine: 'Engine'
    }
  },
  // V24-F103 i18n residue fix: DictManagerLayout headers/status/buttons/aria-label
  dict: {
    colId: 'ID',
    colSort: 'Order',
    colXref: 'Refs',
    colUpdated: 'Updated',
    colStatus: 'Status',
    // 🔧 fix(审查): 字典页 title/subtitle i18n 化 (原硬编码中文)
    pageTitles: {
      oemBrands: { title: 'OEM Brands', subtitle: 'P1.3 Admin · autocomplete for product form section 2' },
      productName1s: { title: 'Product Name 1', subtitle: 'P2.2 Admin · autocomplete for product form section 1' },
      productName2s: { title: 'Product Name 2', subtitle: 'P2.2 Admin · autocomplete for product form section 1' },
      types: { title: 'Types', subtitle: 'P2.2 Admin · fixed 5 values: oil / fuel / air / cabin / others · drag to reorder' },
      oemNo3s: { title: 'OEM No.3', subtitle: 'P2.2 Admin · autocomplete for cross-reference section oem_no_3' },
      medias: { title: 'Media', subtitle: 'P2.2 Admin · 2 fields: Media name + model · product form section 4' },
      machines: { title: 'Machines', subtitle: 'P2.2 Admin · 3 fields: brand + model + name · product form section 7' },
      engines: { title: 'Engines', subtitle: 'P2.2 Admin · 2 fields: brand + type · product form section 7' },
      compare: 'Product Compare',
      errors: 'Error Logs',
      perf: 'Performance Monitor',
      siteContent: 'Site Content',
      // 🔧 fix(审查): 站点内容维护页表单标签 i18n
      site: {
        save: 'Save',
        basic_config: 'Site Basics',
        site_name: 'Site Name',
        about: 'About Us',
        contact: 'Contact Us',
        news: 'News',
        add_news: 'Add News',
        no_news: 'No news yet, click "Add News" to publish the first one',
        delete: 'Delete',
      },
      // 🔧 fix(审查): 产品管理页标题/按钮 i18n
      products: {
        page_title: 'Products',
        batch_compare: 'Batch Compare',
        new_product: 'New Product',
        core: 'Core',
        history: 'History',
      },
      // 🔧 fix(审查): empty-text 拼接后缀
      empty_start: ' to start',
      // 🔧 fix(审查): 字典列表列标题 i18n
      columnLabels: {
        brand: 'Brand',
        productName1: 'Product Name 1',
        productName2: 'Product Name 2',
        mediaName: 'Media Name',
        mediaModel: 'Model',
        model: 'Model',
      },
    },
    bulk: {
      download_template: 'Download Template',
      export_csv: 'Export CSV',
      export_excel: 'Export Excel',
      import_csv: 'Import CSV',
      import_done: 'Import done',
      hint: 'Edit CSV with a plain text editor before importing to avoid Excel auto number conversion (e.g. 00123 → 123, long IDs to scientific notation)',
      export_failed: 'Export failed, retry later',
      import_failed: 'Import failed, check format (each line: value[,sortOrder[,deleted]])',
      import_partial_fail: 'Some rows failed',
    },
    colAction: 'Actions',
    includeDeleted: 'Include deleted',
    statusDeleted: 'Deleted',
    statusActive: 'Active',
    retrying: 'Retrying…',
    retry: 'Retry',
    createButton: 'Add'
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
    turnstileRequired: 'Please complete the verification first',
    turnstileUnavailable: 'Captcha failed to load: add the current domain (e.g. localhost) to your Cloudflare Turnstile settings, then refresh',
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
