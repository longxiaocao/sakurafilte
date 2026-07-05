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
      placeholder: {
        l332_id: '输入产品 ID 加入',
      },
      string: {
        l102_kg: '重量 (kg)',
        l103_mm: '箱长 (mm)',
        l104_mm: '箱宽 (mm)',
        l105_mm: '箱高 (mm)',
        l106_m: '箱体积 (m³)',
        l113_kg: '外箱重 (kg)',
        l114_mm: '外箱长 (mm)',
        l115_mm: '外箱宽 (mm)',
        l116_mm: '外箱高 (mm)',
        l120_crossref: 'CrossRef / 车型',
        l61_1: '产品名 1',
        l62_2: '产品名 2',
        l67_mm: '尺寸 (mm)',
        l86_lr: '旁通 LR',
        l87_hr: '旁通 HR',
        l88_1: '效率 1',
        l89_2: '效率 2',
        l91_bar: '耐压 (bar)',
      },
      warning: {
        l256_id: '请输入有效的产品 ID',
      },
    },
    enginesview: {
      placeholder: {
        l159_cummins: '例: CUMMINS',
        l162_isb_4_5_l: '例: ISB 4.5 L (可空)',
      },
    },
    etlview: {
      label: {
        l605_json: '原始 JSON',
      },
      placeholder: {
        l472_jsonl: 'JSONL 绝对路径',
      },
      string: {
        l275_etl: '取消 ETL 任务',
        l354_: '取消',
        l355_etl: '暂停 ETL 任务',
        l378_etl_n_n_paused_checkpoint_id_1_commit: '恢复暂停的 ETL 任务?\\n\\n将从最近一条 paused 记录的 checkpoint_id+1 行开始续读, 跳过已 COMMIT 的批次.',
        l379_etl: '恢复 ETL 任务',
        l423_insert: 'INSERT 写库',
        l484_truncate_xrefs_apps_products: '开启: TRUNCATE 同时清空 xrefs/apps (首次全量场景); 关闭: 仅清 products, 保留关联表 (单独刷新主表)',
        l81_etl: '松开以填入 ETL 文件路径',
      },
      templatetext: {
        l495_dry_run: '执行 dry-run',
        l613_: '展开全部 ',
        l613_10: '收起 (只显示前 10 行)',
      },
    },
    helpview: {
      string: {
        l127_xlsx: '拖拽 XLSX 到此',
        l15_cross_references_oem_brand_mann_bosch_ma: '替代品牌厂家名 (cross_references.oem_brand), 例: Mann, Bosch, Mahle',
        l16_1: '产品名 1',
        l16_oil_filter_fuel_filter: '产品主名称 (例: Oil Filter, Fuel Filter), 影响前台产品页',
        l17_2: '产品名 2',
        l18_5_oil_fuel_air_cabin_others_sort_order: '5 固定分类: oil / fuel / air / cabin / others, sort_order 决定前台排序',
        l18_type: '类型 (Type)',
        l19_oem_5_27m_distinct_typeahead: '替代品牌 OEM 编号 (5.27M distinct), 字典化便于 typeahead 联想',
        l20_media: '介质 (Media)',
        l21_machine: '机型 (Machine)',
        l22_engine: '发动机 (Engine)',
        l29_oem_oem2_cross_references_oem_brand_oemn: '检查该 OEM 是否在产品表 oem2 字段里 (注意: 不是 cross_references.oem_brand). 前台公开页用 oemNoDisplayt',
        l32_typeahead: '为什么新增产品时 typeahead 联想不到想要的值?',
        l33_r: ')r 排).',
        l36_h1_100_0: '尺寸搜索 (H1=100) 返回 0 条结果, 但库里有这个产品?',
        l41_reading_copy_1m_30_60s_5_output_spike_re: 'reading 阶段是流式 COPY 暂存, 大文件 (1M 行) 可能 30-60s。如超过 5 分钟无进度, 检查后端日志 (output/SPIKE-RE',
        l49_1_ispublished_true_2_slot_1_6_3_console_: '检查 (1) 产品 isPublished=true (上架) (2) slot 1-6 范围 (3) 浏览器 console 看 OSS 预签名 URL 1h',
      },
    },
    machinesview: {
      placeholder: {
        l198_bosch: '例: BOSCH',
        l201_0_451_103_001: '例: 0 451 103 001 (可空)',
        l204_tractor_x300: '例: Tractor X300 (可空)',
        l209_4: '选择 4 大类之一',
      },
    },
    mediasview: {
      label: {
        l159_media: 'Media 名称',
        l162_media: 'Media 型号',
      },
      placeholder: {
        l160_cellulose_synthetic_carbon: '例: Cellulose / Synthetic / Carbon',
      },
      title: {
        l157_media: '新增 Media',
        l157_media_2: '编辑 Media',
      },
    },
    oembrandsview: {
      placeholder: {
        l300_bosch: '例: BOSCH',
      },
      string: {
        l98_: ' 吗? (软删除, 可在',
      },
      title: {
        l295_oem: '新增 OEM 品牌',
        l295_oem_2: '编辑 OEM 品牌',
      },
    },
    oemno3sview: {
      placeholder: {
        l151_11427622448: '例: 11427622448',
      },
      title: {
        l148_oem_3: '新增 OEM 3',
        l148_oem_3_2: '编辑 OEM 3',
      },
      warning: {
        l47_oem_3_200: 'OEM 3 长度不能超过 200',
      },
    },
    productformview: {
      error: {
        l174_oem: '产品已存在, 请检查 OEM 号',
      },
      label: {
        l352_1: '产品名 1',
        l356_2: '产品名 2',
        l431_1: '效率 1',
        l432_2: '效率 2',
        l433_lr: '旁通阀 LR',
        l434_hr: '旁通阀 HR',
        l436_bar: '破裂压力 (bar)',
        l449_kg: '重量 (kg)',
        l453_mm: '箱长 (mm)',
        l457_mm: '箱宽 (mm)',
        l461_mm: '箱高 (mm)',
        l466_m: '箱体积 (m³)',
        l481_kg: '母箱重 (kg)',
        l485_mm: '母箱长 (mm)',
        l489_mm: '母箱宽 (mm)',
        l493_mm: '母箱高 (mm)',
        l497_m: '母箱体积 (m³)',
      },
      placeholder: {
        l427_name_model_or: '输入自动补全 (name/model OR 匹配)',
      },
      title: {
        l404_mm: '③ 尺寸 (mm)',
        l536_1_6: '⑦ 图片 (1-6 槽位)',
      },
    },
    productname1sview: {
      label: {
        l234_1: '产品名 1',
      },
      placeholder: {
        l235_oil_filter: '例: OIL FILTER',
      },
      string: {
        l84_: ' 吗? (软删除, 可在',
      },
      title: {
        l232_1: '新增产品名 1',
        l232_1_2: '编辑产品名 1',
      },
      warning: {
        l61_1: '产品名 1 不能为空',
        l62_1_200: '产品名 1 长度不能超过 200',
      },
    },
    productname2sview: {
      label: {
        l150_2: '产品名 2',
      },
      placeholder: {
        l151_spin_on: '例: SPIN-ON',
      },
      title: {
        l148_2: '新增产品名 2',
        l148_2_2: '编辑产品名 2',
      },
      warning: {
        l46_2: '产品名 2 不能为空',
        l47_2_200: '产品名 2 长度不能超过 200',
      },
    },
    productsview: {
      placeholder: {
        l306_oem_3: 'OEM 3 批量',
        l392_1: '产品名 1',
        l393_2: '产品名 2',
      },
      warning: {
        l237_2_6: '请选择 2-6 个产品',
        l241_6: '最多对比 6 个',
      },
    },
    typesview: {
      placeholder: {
        l161_oil_fuel_air_cabin_others: '例: oil / fuel / air / cabin / others',
      },
      title: {
        l158_type: '新增 Type',
        l158_type_2: '编辑 Type',
      },
      warning: {
        l49_type_50: 'Type 长度不能超过 50',
      },
    },
    usersview: {
      placeholder: {
        l396_8: '至少 8 个字符',
        l463_8: '至少 8 个字符',
      },
      string: {
        l59_admin: '管理员 (admin)',
        l60_operator: '操作员 (operator)',
        l61_viewer: '只读 (viewer)',
      },
      warning: {
        l106_8: '密码至少 8 个字符',
        l194_8: '新密码至少 8 个字符',
      },
    },
  },

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
