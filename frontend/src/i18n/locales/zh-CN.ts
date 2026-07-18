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
  admin: {
    compareview: {
      error: {
        load_product_failed: '加载产品失败',
      },
      placeholder: {
        input_product_id_add: '输入产品 ID 加入',
      },
      string: {


        outer_carton: '外箱',
        outer_carton_pcs: '外箱/件',
        outer_carton_kg: '外箱重 (kg)',
        outer_carton_length_mm: '外箱长 (mm)',
        outer_carton_width_mm: '外箱宽 (mm)',
        outer_carton_height_mm: '外箱高 (mm)',
        crossref_vehicle_model: 'CrossRef / 车型',
        oem_cross_reference: 'OEM 交叉引用',
        machine_applications: '适配车型',
        load_failed: '加载失败',
        basic: '基础',
        oem_number: 'OEM 编号',



        dimensions_mm: '尺寸 (mm)',


        bypass_lr: '旁通 LR',
        bypass_hr: '旁通 HR',



        pressure_resistance_bar: '耐压 (bar)',


        media: '介质',
        media_model: '介质型号',


      },
      success: {
        remove: '已移除',
        added_oem: '已加入: {oem}',
      },
      title: {
        move_left: '左移',
        move_right: '右移',
        remove_columns: '移除该列',
      },
      warning: {
        please_enter_active_product: '请输入有效的产品 ID',
        product_in_compare_list: '该产品已在对比列表中',
        compare_max_max_products: '最多对比 {max} 个产品',
      },
    },
    enginesview: {
      error: {





      },
      label: {



      },
      placeholder: {

        e_g_cummins: '例: CUMMINS',
        e_g_isb_l: '例: ISB 4.5 L (可空)',
      },
      string: {

      },
      success: {





      },
      title: {
        add_engine: '新增发动机',
        edit_engine: '编辑发动机',
      },
      warning: {
        engine_brand_cannot_be_empty: '发动机品牌不能为空',
        engine_brand_length: '发动机品牌长度不能超过 200',

      },
    },
    etlview: {
      page_title: 'ETL 触发与监控',
      section: {
        pipeline: '数据流程',
        trigger: '手动 ETL 触发',
        alert_status: '告警状态',
        last_finished: '最近一次完成结果',
        dry_run: '最近 dry-run 校验',
        recent_errors: '最近错误 (最多 10 条)',
        audit: '取消审计 (按 reason_code 聚合)'
      },
      pipeline: {
        stage_read: '读取',
        stage_staging: '暂存',
        stage_insert: '写入',
        stage_commit: '提交',
        stage_meili: '同步',
        stage_done: '完成',
        stage_idle: '空闲',
        status_running: '运行中',
        status_completed: '已完成',
        status_failed: '失败',
        status_paused: '已暂停',
        status_cancelled: '已取消',
        status_idle: '空闲',
        elapsed_label: '已耗时',
        errors_label: '错误'
      },
      kpi: {
        trigger_24h: '24h 触发总数',
        success_24h: '24h 成功数',
        failed_24h: '24h 失败数',
        avg_duration: '24h 平均耗时',
        last_24h: '最近 24 小时',
        success_rate: '成功率 {rate}%',
        need_attention: '需关注',
        all_ok: '全部正常',
        completed_only: '仅统计已完成任务'
      },
      alert: {
        p2_tag: 'P2 待接入',
        title: '告警系统',
        description: '告警系统 (钉钉 / 微信 / 通用 Webhook / 微信公众号) 已接入。',
        planned_types: '告警类型',
        planned_channels: '推送渠道',
        type_etl: 'ETL 任务',
        type_perf: '性能阈值',
        type_security: '安全事件',
        type_access: '访问异常',
        type_resource: '资源监控',
        channel_dingtalk: '钉钉',
        channel_wechat: '企业微信',
        channel_webhook: '通用 Webhook',
        view_design_btn: '查看告警设计文档',
        // P2-1 真实告警 KPI
        '7d_failed': '7 日失败',
        '7d_p0': '7 日 P0',
        '7d_sent': '7 日已发',
        latest: '最近一次',
        no_history: '暂无告警历史',
        view_all_btn: '查看告警中心'
      },
      audit: {
        observable_tag: '运营可观察',
        recent_20_cancelled: '最近 20 条 cancelled 记录',
        reason_code: '原因码',
        legacy: '历史数据'
      },
      dry_run: {
        samples_count: '样本 {count} 行',
        samples_preview: '样本预览 (前 {count} 行 JSON)'
      },
      buttontext: {
        next: '下一步',

        confirm_cancel: '确认取消',

        pause: '暂停',
        no_pause: '不暂停',

        no_resume: '不恢复',
        resume: '恢复',
      },
      info: {
        description_empty_default: '可补充详细描述 (留空用默认)',
        cancel_note: '取消原因说明',

        task_pause: '无活跃任务可暂停',
      },
      label: {
        entity: '实体',

        file: '文件路径',
        file_v2: '文件',
        en: '大小',
        rows_count: '行数',

        original_json: '原始 JSON',
        timestamp: '时间',
        error: '错误',
        en_v2: '原因',
        phrase_63454: '已读/插/改',
        en_v3: '耗时',
        cancel_timestamp: '取消时间',
      },
      placeholder: {
        jsonl_absolute_path: 'JSONL 绝对路径',
      },
      string: {
        sse_on_browser_will: 'SSE 连接断开, 浏览器将自动重连',




        task_timeout: '任务超时',
        task_execute: '任务执行超时',
        system_shutdown_restart: '系统关闭/重启',
        service_close_restart: '服务关闭/重启',


        cancel_etl_task: '取消 ETL 任务',


        pause_etl_task: '暂停 ETL 任务',

        pause_current_etl_task: '暂停当前 ETL 任务?\n\n当前批次跑完后会优雅退出, checkpoint_id 会写入 etl_progress_log, 后续可用"{resume}"按钮从该点续读.\n\n(区别于"{cancel}" — 取消会立即终止并回滚当前批次)',

        resume_pause_etl_task: '恢复暂停的 ETL 任务?\n\n将从最近一条 paused 记录的 checkpoint_id+1 行开始续读, 跳过已 COMMIT 的批次.',
        resume_etl_task: '恢复 ETL 任务',
        resume_triggered_entity_entity: '已触发 Resume: entity={entity} checkpoint={checkpoint} (从第 {line} 行开始)',
        resume_triggered_entity_entity_alt: '已触发 Resume: entity={entity} checkpoint={checkpoint} (从第 {line} 行继续)',
        copy_staging: 'COPY 暂存',
        insert_write_db: 'INSERT 写库',
        commit_submit: 'COMMIT 提交',
        meili_sync: 'Meili 同步',
        complete: '完成',
        on_truncate_clear_xrefs: '开启: TRUNCATE 同时清空 xrefs/apps (首次全量场景); 关闭: 仅清 products, 保留关联表 (单独刷新主表)',
        auto_recognized_entity_entity: '已自动识别 entity={entity}, 文件: {name}',
        file_filled_name_entity: '已填入文件: {name} (entity 需手动选择)',
        dropped_total_files_only: '本次拖入 {total} 个文件, 仅采用第一个: {name}',
        on_etl_file: '松开以填入 ETL 文件路径',

        cancel_signal_sent_code: '已发送取消信号 (码: {code}), 任务即将终止',
      },
      success: {
        dry_run_validation_completed: 'dry-run 校验完成',
        triggered_etl_background_execute: '已触发 ETL, 后台执行中',
        phrase_21459: '已清除',
      },
      templatetext: {
        immediately_import: '立即导入',
        execute_dry_run: '执行 dry-run',
        expand_all: '展开全部 {count} 行',
        collapse_show_front_rows: '收起 (只显示前 10 行)',
        cancel_task: '取消任务',
      },
      warning: {

      },
    },
    alertsview: {
      page_title: '告警中心',
      btn: {
        test: '测试告警',
        rules: '告警规则'
      },
      kpi: {
        total_7d: '7 日总数',
        sent: '已发送',
        failed: '失败',
        suppressed: '已抑制',
        p0: 'P0 严重',
        p1: 'P1 数据',
        last_7d: '最近 7 天',
        send_success: '推送成功',
        send_failed: '推送失败',
        suppressed_in_window: '抑制窗口内',
        severity_p0: '严重度 P0',
        severity_p1: '严重度 P1'
      },
      filter: {
        type: '类型',
        severity: '严重度',
        status: '状态',
        all: '全部'
      },
      table: {
        title: '告警历史',
        records: '条记录',
        severity: '严重度',
        type: '类型',
        title_col: '标题',
        channel: '渠道',
        status: '状态',
        sent_at: '发送时间',
        actions: '操作',
        detail: '详情'
      },
      detail: {
        title: '告警详情',
        id: 'ID',
        severity_type: '严重度 / 类型',
        title_col: '标题',
        channel_status: '渠道 / 状态',
        sent_at: '发送时间',
        recipients: '接收人',
        error: '失败原因',
        content: '完整 Payload',
        response: '渠道响应'
      },
      rules: {
        title: '告警规则',
        empty: '暂无规则 (需在 alert_rules 表中插入或调用后端 API)',
        type: '类型',
        severity: '严重度',
        channels: '渠道',
        enabled: '启用'
      },
      test: {
        triggered_by_user: '当前用户手动触发'
      }
    },
    helpview: {
      string: {
        xlsx_to: '拖拽 XLSX 到此',
        search: '搜索',
        alternative_brand_cross_references: '替代品牌厂家名 (cross_references.oem_brand), 例: Mann, Bosch, Mahle',

        product_name_e_g: '产品主名称 (例: Oil Filter, Fuel Filter), 影响前台产品页',

        product_name_model_back: '产品副名称/型号后缀 (例: OF100)',
        category_oil_fuel_air: '5 固定分类: oil / fuel / air / cabin / others, sort_order 决定前台排序',
        type_type: '类型 (Type)',
        alternative_brand_oem_number: '替代品牌 OEM 编号 (5.27M distinct), 字典化便于 typeahead 联想',
        filter_media_name_model: '滤材名称 + 型号 (2 字段字典), 例: Cellulose / A020',
        media_media: '介质 (Media)',
        machine_brand_model_name: '机器品牌 + 型号 + 名称, 按 4 大类聚合: Agriculture / Commercial / Construction / others',
        machine_model_machine: '机型 (Machine)',
        engine_brand_model: '发动机品牌 + 型号',
        engine_engine: '发动机 (Engine)',
        for_input_oem_number: '为什么输入 OEM 编号后无法搜索?',
        check_if_oem_is: '检查该 OEM 是否在产品表 oem2 字段里 (注意: 不是 cross_references.oem_brand). 前台公开页用 oemNoDisplay / oem2, 后台搜索用任意一个字段.',
        oem_yes_no_in: '检查该 OEM 是否在产品表 oem2 字段里 (注意: 不是 cross_references.oem_brand). 前台公开页用 oemNoDisplay',
        for_add_product_typeahead: '为什么新增产品时 typeahead 联想不到想要的值?',
        dictionary_is_maintained_in: '字典是后台维护的, 需先在 "字典管理" → 对应字典 → 新增 value. typeahead 只返回字典内已存在的值 (前 20 条按 sort_order 排).',
        dictionary_management: '字典管理',
        dimensions_search_h_back: '尺寸搜索 (H1 = 100) 返回 0 条结果, 但库里有这个产品?',
        dimensions_search_default_mm: '尺寸搜索默认容差 ±5mm (固定, 不可改), 即 95-105 之间. 如果产品 H1 = 110, 不会命中. 改用更小的 H1 值或精确 ID 查询.',
        etl_trigger_back_in: 'ETL 触发后卡在 reading 状态?',
        reading_phase_is_streaming: 'reading 阶段是流式 COPY 暂存, 大文件 (1M 行) 可能 30-60s. 如超过 5 分钟无进度, 检查后端日志 (output/SPIKE-REPORT-*.md) 看是否有 SQL 错误.',
        batch_delete_product: '怎么批量删除产品?',
        in_admin_product_list: '后台产品列表勾选多行 → 顶部 "批量停售" 按钮. 停售 = is_discontinued=true, 前台不展示, 历史数据保留. 如需物理删除, 走 SQL (慎用).',
        upload_image_back_frontend_sho: '上传图片后前台不显示?',
        check_product_ispublished_true: '检查 (1) 产品 isPublished=true (上架) (2) slot 1-6 范围 (3) 浏览器 console 看 OSS 预签名 URL 1 h 有效. 如过期, 重新加载产品页.',
        product_ispublished_true_listed: '检查 (1) 产品 isPublished=true (上架) (2) slot 1-6 范围 (3) 浏览器 console 看 OSS 预签名 URL 1 ',
        enter_admin: '进入后台',
        mode_full_load_insert: ' + 模式 (full-load / insert-only / upsert), 点 ',
      },
      title: {
        start: '快速开始',
        en_v4: '字典使用规范',
        batch_import: '批量导入',
        search_v2: '搜索容差',
        common: '常见问题',
      },
    },
    machinesview: {
      error: {





      },
      label: {



        category: '分类',

      },
      placeholder: {

        e_g_empty: '例: 0 451 103 001 (可空)',
        e_g_tractor_x: '例: Tractor X300 (可空)',
        select: '选择 4 大类之一',
      },
      string: {

      },
      success: {





      },
      title: {
        add_machine_model: '新增机型',
        edit_machine_model: '编辑机型',
      },
      warning: {
        machine_model_brand_cannot_be: '机型品牌不能为空',
        machine_model_brand_length: '机型品牌长度不能超过 200',

      },
    },
    mediasview: {
      error: {





      },
      label: {
        media_name: 'Media 名称',
        media_model: 'Media 型号',

      },
      placeholder: {
        search_media_name_or: '搜索 Media 名称或型号',
        e_g_cellulose_synthetic: '例: Cellulose / Synthetic / Carbon',
        e_g_m_m: '例: 5μm / 10μm (可空)',
      },
      string: {

      },
      success: {





      },
      title: {
        add_media: '新增 Media',
        edit_media: '编辑 Media',
      },
      warning: {
        media_name_cannot_be: 'Media 名称不能为空',
        media_name_length: 'Media 名称长度不能超过 100',

      },
    },
    oembrandsview: {
      error: {

      },
      label: {


      },
      placeholder: {
        search_brand: '搜索品牌',

      },
      string: {



        add_brand: '新增品牌',


      },
      success: {





      },
      title: {

        add_oem_brand: '新增 OEM 品牌',
        edit_oem_brand: '编辑 OEM 品牌',
      },
      warning: {
        brand_cannot_be_empty: '品牌名不能为空',
        brand_length: '品牌名长度不能超过 100',
      },
    },
    oemno3sview: {
      error: {





      },
      label: {

      },
      placeholder: {
        search_oem: '搜索 OEM 3',
        e_g: '例: 11427622448',
      },
      string: {

      },
      success: {





      },
      title: {
        add_oem: '新增 OEM 3',
        edit_oem: '编辑 OEM 3',
      },
      warning: {
        oem_cannot_be_empty: 'OEM 3 不能为空',
        oem_length: 'OEM 3 长度不能超过 200',

      },
    },
    perfview: {
      label: {
        pause_auto_refresh: '暂停自动刷新',
        on_auto_refresh: '开启自动刷新',
        refresh: '刷新间隔',
      },
      string: {
        p_ms_ms_ms: 'P95 = {ms}ms (≥1000ms 严重)',
        p_ms_ms_ms_v2: 'P95 = {ms}ms (≥500ms 警告)',
        error_rate_pct_critical: '错误率 = {pct}% (≥10% 严重)',
        error_rate_pct_warning: '错误率 = {pct}% (≥5% 警告)',

        en_v5: '就绪',
        downgrade: '降级',

        refresh_failed: '刷新失败',
      },
      templatetext: {
        pause_v2: '⏸ 暂停',
        refresh_v2: '刷新中…',
        refresh: '↻ 刷新',
        alert: '⚠ 严重告警',
        warning: '⚠ 警告',

        en: '存活',

        en_appsettings_json: 'appsettings.json (兜底)',
        db_load: 'DB (已加载)',
      },
    },
    productformview: {
      error: {
        data_has_been_modified_by: '数据已被其他管理员修改, 请刷新后重试',
        product_already_exists_please: '产品已存在, 请检查 OEM 号',



      },
      label: {



        oem_required: 'OEM 2 (必填)',

        remark: '备注',




        bypass_valve_lr: '旁通阀 LR',
        bypass_valve_hr: '旁通阀 HR',

        collapse_pressure_bar: '破裂压力 (bar)',



        master_box_qty: '母箱数量',
        master_carton_kg: '母箱重 (kg)',
        master_carton_length_mm: '母箱长 (mm)',
        master_carton_width_mm: '母箱宽 (mm)',
        master_carton_height_mm: '母箱高 (mm)',
        master_box_volume_m: '母箱体积 (m³)',
      },
      placeholder: {


        brand_input_auto: '品牌 (输入自动补全)',
        oem_input_auto: 'OEM 3 (输入自动补全)',

        input_auto_name_model: '输入自动补全 (name/model OR 匹配)',


        brand_required: '品牌 (必填)',
        model_required: '型号 (必填)',


        engine_model: '发动机型号',
      },
      string: {
        by_modify: '已被修改',
        by_user_modify: '已被其他用户修改',
        slot_slot_uploaded: 'Slot {slot} 上传成功',
        slot_slot_deleted: 'Slot {slot} 已删除',
        edit_product_id: '编辑产品 #{id}',
        cross_reference_count: '② 交叉引用 ({count})',
        machine_applications_count: '⑥ 适用车型 ({count})',
      },
      success: {
        saved: '已保存',
        created: '已创建',
      },
      templatetext: {
        add_product: '新增产品',
      },
      title: {
        basic_info: '基础信息',
        dimensions_mm: '③ 尺寸 (mm)',

        image: '⑦ 图片 (1-6 槽位)',
      },
      warning: {
        please_first_save_product_then: '请先保存产品再上传图片',
      },
    },
    productname1sview: {
      error: {

      },
      label: {


      },
      placeholder: {
        search_product_name: '搜索产品名 1',
        e_g_oil_filter: '例: OIL FILTER',
      },
      string: {





      },
      success: {





      },
      title: {

        add_product: '新增产品名 1',
        edit_product: '编辑产品名 1',
      },
      warning: {
        product_name_cannot_be: '产品名 1 不能为空',
        product_name_length: '产品名 1 长度不能超过 200',
      },
    },
    productname2sview: {
      error: {





      },
      label: {

      },
      placeholder: {
        search_product_name: '搜索产品名 2',
        e_g_spin_on: '例: SPIN-ON',
      },
      string: {

      },
      success: {





      },
      title: {
        add_product: '新增产品名 2',
        edit_product: '编辑产品名 2',
      },
      warning: {
        product_name_cannot_be: '产品名 2 不能为空',
        product_name_length: '产品名 2 长度不能超过 200',

      },
    },
    productsview: {
      aria: {
        oem_search: 'OEM 2 搜索',
        mr_search: 'MR.1 搜索',
        product_name_search: '产品名搜索',
        filter_by_type: '按类型筛选',
        oem_batch_search: 'OEM 3 批量搜索',
      },
      label: {



        discontinued: '停售',
        update: '更新',
        action: '操作',

        field: '字段',
        value: '新值',
      },
      placeholder: {

        oem_batch_count: 'OEM 3 批量',



        efficiency: '效率',




      },
      string: {
        all_columns: '全部列',
        columns: '核心列',
      },
      success: {
        discontinued_v2: '已停售',

      },
      title: {
        filter: '高级筛选',
        en_v6: '变更历史',
      },
      warning: {

        please_select_pcs_product: '请选择 2-6 个产品',
        at_most_compare_pcs: '最多对比 6 个',
      },
    },
    typesview: {
      error: {





      },
      label: {

      },
      placeholder: {
        search_type: '搜索 Type',
        e_g_oil_fuel: '例: oil / fuel / air / cabin / others',
      },
      string: {


      },
      success: {




        sort_order_saved_frontend: '排序已保存, 前台产品页 P2.3 立即生效',
      },
      title: {
        add_type: '新增 Type',
        edit_type: '编辑 Type',
      },
      warning: {
        type_cannot_be_empty: 'Type 不能为空',
        type_length: 'Type 长度不能超过 50',
      },
    },
    usersview: {
      label: {
        user_list: '用户列表',
        login_audit: '登录审计',

        password: '密码',



        enable_status: '启用状态',
        password_v2: '新密码',
      },
      placeholder: {
        login_username: '登录用户名',


      },
      string: {

        password_of_user_has: '已重置 {user} 的密码',
        admin_admin: '管理员 (admin)',
        action_operator: '操作员 (operator)',
        read_only_viewer: '只读 (viewer)',
      },
      success: {
        user_created: '用户已创建',
        user_updated: '用户已更新',

        logout: '已退出登录',
      },
      title: {
        add_user: '新增用户',
        edit_user_user: '编辑用户: {user}',
        reset_password_user: '重置密码: {user}',
      },
      warning: {
        password_at_least_pcs: '密码至少 8 个字符',
        password_at_least_pcs_v2: '新密码至少 8 个字符',
        username_cannot_be_empty: '用户名不能为空',
      },
    },
    // V24-F72: 补充 errorview aria key (AdminErrorView 用)
    errorview: {
      aria: {
        trigger_test_error: '触发测试错误',
      },
    },
  },

  common: {

    field: {

      soft_delete_confirm: ' 吗? (软删除, 可在',

      slot_must_be_1_to_6: ', 必须在 1-6 之间',

      d7_thread: 'D7 螺纹',

      d8_thread: 'D8 螺纹',

      oem_brand: 'OEM 品牌',

      invalid_slot: 'Slot 非法: ',

      no_cancel: '不取消',

      unlimited: '不限',

      product_name: '产品名',

      e_g_bosch: '例: BOSCH',

      full_name: '全名',

      all: '全部',

      other_reason: '其他原因',

      packaging: '包装',

      check_valve_count: '单向阀数',

      engine_brand: '发动机品牌',

      publish: '发布',

      cancel: '取消',

      performance: '性能',

      drag_to_sort: '拖动以排序',

      search_any_field: '搜索任一字段',

      fault: '故障',

      ready: '就绪',

      efficiency_1: '效率 1',

      efficiency_2: '效率 2',

      bypass_pressure: '旁通压力',

      bypass_valve_count: '旁通阀数',

      downgrade: '降级',

      no_active_task_to_cancel: '无活跃任务可取消',

      detecting: '检测中',

      mode: '模式',

      temperature_range: '温度范围',

      user_cancelled: '用户主动取消',

      username: '用户名',

      admin_force_cancel: '管理员强制取消',

      carton_volume_m3: '箱体积 (m³)',

      carton_width_mm: '箱宽 (mm)',

      carton_length_mm: '箱长 (mm)',

      carton_height_mm: '箱高 (mm)',

      auto_calculated: '自动计算',

      at_least_8_chars: '至少 8 个字符',

      role: '角色',

      input_autocomplete: '输入自动补全',

      email: '邮箱',

      weight_kg: '重量 (kg)',
    },
      error_001: '服务器繁忙,请稍后重试 (错误码:${status})',
      error_002: '网络连接失败,请检查网络',
      error_003: '请输入密码',
      error_004: '请输入当前密码',
      error_005: '请输入用户名',
      info_001: 'OEM 查询',
      info_002: '即将触发 ${entity} ETL (${mode}${dryRun ? \', 试运行\' : \'\'}), 是否继续?',
      info_003: '搜索 message / type / tags…',
      info_004: '搜索路径 / 方法 / 摘要…',
      info_005: '粘贴 OEM 编号, 每行一个 (支持 tab/换行/逗号/分号分隔)&#10;例如:&#10;OEN-123&#10;AB/CD/456&#10;滤清器 1142',
      info_006: '网络异常: ${err.message || \'请稍后重试\'}',
      info_007: '请求频率超限, 请 ${retryAfter || 60}s 后重试',
      info_008: '输入 045090 试试',
      info_009: '输入产品 ID 加入',
      success_001: '已加入: ${data.items[0].oemNoDisplay}',
      success_002: '已发送暂停信号 (码: ${code}), 任务即将终止',
      warn_001: '再次输入新密码',
      warn_002: '最多 500 个 OEM, 当前 ${oems.length}',
      warn_003: '最多对比 ${MAX_COMPARE} 个产品',
      warn_004: '清空确认',
      warn_005: '确定停售产品 ',
      warn_006: '确定删除 ',
      warn_007: '确定删除品牌 ',
      warn_008: '确定删除用户 ',
      warn_009: '确认',
      warn_010: '至少 8 个字符',
    
    action: {

      product_name_1: '产品名 1',

      product_name_2: '产品名 2',

      type: '类型',

      seal_material: '密封材料',

      carton_per_pcs: '箱/件',

      load_failed: '加载失败: ',

      operation_failed: '操作失败',

      delete_failed: '删除失败',

      restore_failed: '恢复失败',

      sort_failed: '排序失败',

      brand: '品牌',

      model: '型号',

      sort_order: '排序',

      no_data_click_top_right: '>暂无数据, 点击右上',

      created: '已新增',

      updated: '已更新',

      deleted: '已删除',

      restored: '已恢复',

      sort_order_saved: '排序已保存',

      ready: '就绪',

      confirm: '确认',

      resume: '恢复',

      name: '名称',

      optional: '可选',

      // V24-F72: 补充 common.action 下缺失的通用操作 key
      //   WHY: AdminAlertsView/AdminEtlView 用 t('common.action.search/reset/refresh') 但 i18n 里缺失
      search: '搜索',
      reset: '重置',
      refresh: '刷新',
    },
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
    loadFailed: '加载失败, 请稍后重试或联系管理员',
    // P-Admin-UX: 通用反馈提示 (ElMessage.success/warning/error/info)
    //   前缀按严重程度分类: error/success/info/warn
    //   与 vue-i18n 配合: 缺失 key 默认回退到原 key 字符串, 故必须显式声明
    feedback: {
      // ----- 错误提示 -----
      error_002: '导出失败',
      error_003: '最多添加 6 个产品到对比',
      error_008: '复制失败',
      error_009: '清空失败',
      error_018: '清空失败',
      error_022: '加载 API 文档失败, 请检查后端 Swagger 状态',
      error_023: '加载本地 API 文档失败',
      error_029: '请求失败, 请检查网络',
      error_045: '请输入搜索关键词',
      error_048: '请先添加要对比的产品',
      // ----- 成功提示 -----
      success_002: '已复制到剪贴板',
      success_010: '密码修改成功',
      success_012: '已加载本地缓存的 API 文档',
      success_014: '已重新加载错误日志',
      success_015: '已清空所有错误日志',
      success_016: '已在对比列表中, 跳转查看',
      success_019: '已退出登录',
      // ----- 信息提示 -----
      info_004: 'OEM 编号不能为空',
      info_005: '权限不足, 已跳到产品管理',
      info_017: '已加入对比',
      info_024: '未找到该 OEM 对应的产品',
      info_030: '网络错误, 请检查连接',
      info_041: '请输入产品 ID',
      info_042: '请先登录',
      info_043: '登录已过期, 请重新登录',
      // ----- 警告提示 -----
      warn_040: '已选满 6 个对比, 请先移除'
    },
    dictviewcommon: {
      total_drag: '共 {total} 条 (启用 {active}, 软删 {soft}) · 拖动以排序',
    },
    // V24-F43 (spec Task 0.5.6/F3-4): 后端 errorCode → 中文友好提示映射
    //   WHY: http.ts 拦截器 fallback 链 i18n.global.t('common.error.' + errorCode) 查找此表
    //   未命中的 errorCode 回退到 ERROR_CODE_MAP[status] → data.title → 请求失败 (status)
    error: {
      // ===== 旧 ERR_ 前缀错误码 (10 个) =====
      ERR_VALIDATION_FAILED: '请求参数验证失败',
      ERR_NOT_FOUND: '请求的资源不存在',
      ERR_CONFLICT: '资源已存在或冲突',
      ERR_FORBIDDEN: '没有权限执行此操作',
      ERR_CANCELLED: '请求已取消',
      ERR_INTERNAL: '服务器内部错误,请稍后重试',
      ERR_DB_CONFLICT: '数据冲突 (可能被其他用户修改),请刷新重试',
      ERR_DB_CONSTRAINT: '数据约束失败 (外键或非空校验)',
      ERR_DB_TIMEOUT: '数据库繁忙,请稍后重试',
      ERR_AUTH_FAILED: '用户名或密码错误',
      // ===== V2 错误码 (15 个,无 ERR_ 前缀) =====
      MR1_REQUIRED: 'MR.1 编号必填',
      MR1_FORMAT_INVALID: 'MR.1 编号格式无效',
      MR1_ALREADY_EXISTS: 'MR.1 编号已存在',
      OEM3_ALREADY_EXISTS: 'OEM 3 编号已存在',
      MACHINE_TYPE_INVALID: '机型类型无效',
      XREF_CONFLICT: '交叉引用冲突 (可能被其他用户修改),请刷新重试',
      SEARCH_PAGE_TOO_DEEP: '搜索页数过深,请重新搜索',
      CURSOR_INVALID: '分页游标无效,已重置到第 1 页',
      CURSOR_EXPIRED: '分页游标已过期,已重置到第 1 页',
      IMAGE_ROLE_SLOT_MISMATCH: '图片角色与槽位不匹配',
      IMAGE_DETAIL_SLOT_INVALID: '图片详情槽位无效 (必须在 1-6 之间)',
      IMAGE_PRIMARY_DUPLICATE: '主图已存在 (每个产品仅允许 1 张主图)',
      IMAGE_DETAIL_SLOT_DUPLICATE: '图片详情槽位重复',
      MR1_NOT_FOUND: 'MR.1 编号不存在',
      OEM3_NOT_FOUND: 'OEM 3 编号不存在',
    },
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
    singleTitle: '单条搜索',
    // 公开搜索页入口: 8 字段联想 + 明细表 + 加入对比 (面向客户/外网)
    advancedSearch: '高级搜索'
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
    timestamp: '时间',
  },
  a11y: {
    skipToContent: '跳到主内容',
  },
}
