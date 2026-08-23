import re
text = open(r'../frontend/src/i18n/locales/zh-CN.ts', encoding='utf-8').read()
for k in ['admin.helpview.string.l26_oem_oem2_cross_references_oem_brand_oemn',
          'admin.helpview.string.l38_reading_copy_1m_30_60s_5_output_spike_r',
          'admin.helpview.string.l46_1_ispublished_true_2_slot_1_6_3_console_']:
    pat = "'" + re.escape(k) + "':\\s*'([^']*)'"
    m = re.search(pat, text)
    if m:
        print(k)
        print('  => ' + m.group(1))
    else:
        print(k, 'NOT FOUND')
