"""批量修复剩余硬编码中文
WHY: 之前 1029 处中已替换 992 处, 剩 37 处多为:
  - ElMessage 提示 (用户可见)
  - 重复的 template 文本 (5 个 view 共用 '共 N 条 (启用 M, 软删 K) · 拖动')
  - 4 个 el-table-column title
  - 5 个 placeholder + aria-label (AdminProductsView)
  - 2 个 ElMessage.success (ProductForm: Slot 上传/删除)
"""
import re
from pathlib import Path

ROOT = Path('frontend/src')

def fix_admin_etl():
    """AdminEtlView.vue - 3 个 ElMessage + 2 个 string + 1 个 success"""
    p = ROOT / 'views/admin/AdminEtlView.vue'
    src = p.read_text(encoding='utf-8')
    orig = src
    # 已自动识别 entity=${inferred}, 文件: ${f.name}
    src = src.replace(
        "ElMessage.success(`已自动识别 entity=${inferred}, 文件: ${f.name}`)",
        "ElMessage.success(t('admin.etlview.string.l64_auto_inferred', { entity: inferred, name: f.name }))"
    )
    # 已填入文件: ${f.name} (entity 需手动选择)
    src = src.replace(
        "ElMessage.info(`已填入文件: ${f.name} (entity 需手动选择)`)",
        "ElMessage.info(t('admin.etlview.string.l68_manual_entity', { name: f.name }))"
    )
    # 本次拖入 ${files.length} 个文件, 仅采用第一个: ${f.name}
    src = src.replace(
        "ElMessage.warning(`本次拖入 ${files.length} 个文件, 仅采用第一个: ${f.name}`)",
        "ElMessage.warning(t('admin.etlview.string.l71_first_only', { total: files.length, name: f.name }))"
    )
    # 已发送取消信号 (码: ${reasonCode}), 任务即将终止
    src = src.replace(
        "ElMessage.warning(`已发送取消信号 (码: ${reasonCode}), 任务即将终止`)",
        "ElMessage.warning(t('admin.etlview.string.l323_cancel_signal', { code: reasonCode }))"
    )
    # 已触发 Resume: entity=${r.entity} checkpoint=${r.checkpointId} (从第 ${r.nextLineNo}
    src = src.replace(
        "ElMessage.success(`已触发 Resume: entity=${r.entity} checkpoint=${r.checkpointId} (从第 ${r.nextLineNo} 行继续)`)",
        "ElMessage.success(t('admin.etlview.string.l386_resume', { entity: r.entity, checkpoint: r.checkpointId, line: r.nextLineNo }))"
    )
    # string "恢复" / "取消"  (line 354)
    src = src.replace(
        '>恢复</el-button>',
        '>{{ t("admin.etlview.string.l353_resume") }}</el-button>'
    )
    src = src.replace(
        '>取消</el-button>',
        '>{{ t("admin.etlview.string.l353_cancel") }}</el-button>'
    )
    if src != orig:
        p.write_text(src, encoding='utf-8')
        return True
    return False

def fix_admin_compare():
    """AdminCompareView.vue - 2 个 ElMessage"""
    p = ROOT / 'views/admin/AdminCompareView.vue'
    src = p.read_text(encoding='utf-8')
    orig = src
    src = src.replace(
        "ElMessage.warning(`最多对比 ${MAX_COMPARE} 个产品`)",
        "ElMessage.warning(t('admin.compareview.warning.l262_max', { max: MAX_COMPARE }))"
    )
    src = src.replace(
        "ElMessage.success(`已加入: ${p.oemNoDisplay}`)",
        "ElMessage.success(t('admin.compareview.success.l272_added', { oem: p.oemNoDisplay }))"
    )
    if src != orig:
        p.write_text(src, encoding='utf-8')
        return True
    return False

def fix_admin_perf():
    """AdminPerfView.vue - 4 个 string (P95/错误率)"""
    p = ROOT / 'views/admin/AdminPerfView.vue'
    src = p.read_text(encoding='utf-8')
    orig = src
    # 4 个 P95/错误率模板
    src = src.replace(
        '`P95 = ${p.p95Ms.toFixed(0)}ms (≥1000ms 严重)`',
        't("admin.perfview.string.l150_p95_crit", { ms: p.p95Ms.toFixed(0) })'
    )
    src = src.replace(
        '`P95 = ${p.p95Ms.toFixed(0)}ms (≥500ms 警告)`',
        't("admin.perfview.string.l152_p95_warn", { ms: p.p95Ms.toFixed(0) })'
    )
    src = src.replace(
        '`错误率 = ${p.errorRate.toFixed(1)}% (≥10% 严重)`',
        't("admin.perfview.string.l155_err_crit", { pct: p.errorRate.toFixed(1) })'
    )
    src = src.replace(
        '`错误率 = ${p.errorRate.toFixed(1)}% (≥5% 警告)`',
        't("admin.perfview.string.l157_err_warn", { pct: p.errorRate.toFixed(1) })'
    )
    if src != orig:
        p.write_text(src, encoding='utf-8')
        return True
    return False

def fix_admin_product_form():
    """AdminProductFormView.vue - 2 个 ElMessage + 1 个 template-text + 2 个 title"""
    p = ROOT / 'views/admin/AdminProductFormView.vue'
    src = p.read_text(encoding='utf-8')
    orig = src
    src = src.replace(
        "ElMessage.success(`Slot ${slot} 上传成功`)",
        "ElMessage.success(t('admin.productformview.string.l291_slot_uploaded', { slot }))"
    )
    src = src.replace(
        "ElMessage.success(`Slot ${slot} 已删除`)",
        "ElMessage.success(t('admin.productformview.string.l312_slot_deleted', { slot }))"
    )
    # 编辑产品 #${productId}
    src = src.replace(
        "编辑产品 #${productId}",
        "{{ t('admin.productformview.string.l340_edit_product', { id: productId }) }}"
    )
    # ② 交叉引用 (${form.crossReferences.length})
    src = src.replace(
        "② 交叉引用 (${form.crossReferences.length})",
        "t('admin.productformview.string.l375_xrefs', { count: form.crossReferences.length })"
    )
    # ⑥ 适用车型 (${form.machineApplications.length})
    src = src.replace(
        "⑥ 适用车型 (${form.machineApplications.length})",
        "t('admin.productformview.string.l510_apps', { count: form.machineApplications.length })"
    )
    if src != orig:
        p.write_text(src, encoding='utf-8')
        return True
    return False

def fix_admin_users():
    """AdminUsersView.vue - 1 ElMessage.success + 2 title"""
    p = ROOT / 'views/admin/AdminUsersView.vue'
    src = p.read_text(encoding='utf-8')
    orig = src
    src = src.replace(
        "ElMessage.success(`已重置 ${resetForm.username} 的密码`)",
        "ElMessage.success(t('admin.usersview.string.l199_reset_pwd', { user: resetForm.username }))"
    )
    src = src.replace(
        "编辑用户: ${editForm.username}",
        "t('admin.usersview.title.l424_edit_user', { user: editForm.username })"
    )
    src = src.replace(
        "重置密码: ${resetForm.username}",
        "t('admin.usersview.title.l456_reset_pwd', { user: resetForm.username })"
    )
    if src != orig:
        p.write_text(src, encoding='utf-8')
        return True
    return False

def fix_admin_products_aria():
    """AdminProductsView.vue - 5 个 aria-label (placeholder 已 OK)"""
    p = ROOT / 'views/admin/AdminProductsView.vue'
    src = p.read_text(encoding='utf-8')
    orig = src
    # 5 个 aria-label 已经是 'OEM 2 搜索' 等, 但要转成 t() 形式
    # 占位符 placeholder 已经是 t() 形式
    # aria-label 应该是 'aria-label="OEM 2 搜索"' 形式, 改成 t() 形式
    src = src.replace(
        'aria-label="OEM 2 搜索"',
        ':aria-label="t(\'admin.productsview.aria.l297_oem2\')"'
    )
    src = src.replace(
        'aria-label="MR.1 搜索"',
        ':aria-label="t(\'admin.productsview.aria.l298_mr1\')"'
    )
    src = src.replace(
        'aria-label="产品名搜索"',
        ':aria-label="t(\'admin.productsview.aria.l299_product_name\')"'
    )
    src = src.replace(
        'aria-label="按类型筛选"',
        ':aria-label="t(\'admin.productsview.aria.l300_type\')"'
    )
    src = src.replace(
        'aria-label="OEM 3 批量搜索"',
        ':aria-label="t(\'admin.productsview.aria.l307_oem3_batch\')"'
    )
    if src != orig:
        p.write_text(src, encoding='utf-8')
        return True
    return False

def fix_dict_template_text():
    """5 个 dict view 共用模板: '共 {{ total }} 条 (启用 {{ activeCount }}, 软删 {{ total - activeCount }}) · 拖动'"""
    files = [
        'AdminEnginesView.vue',
        'AdminMachinesView.vue',
        'AdminMediasView.vue',
        'AdminOemNo3sView.vue',
        'AdminProductName2sView.vue',
        'AdminTypesView.vue',
    ]
    changed = 0
    for fn in files:
        p = ROOT / f'views/admin/{fn}'
        if not p.exists():
            continue
        src = p.read_text(encoding='utf-8')
        orig = src
        # 替换模板文本 (注意: .vue 中 {{ }} 不会和 JS 模板字符串冲突)
        old = '>共 {{ total }} 条 (启用 {{ activeCount }}, 软删 {{ total - activeCount }}) · 拖动'
        new = '>{{ t("admin.dictviewcommon.total_drag", { total, active: activeCount, soft: total - activeCount }) }}'
        if old in src:
            # 需要先看上下文决定是否能用 t() - 这个是 template-text, 直接调用 t() 即可
            # 但 i18n key 必须是 'common.dictviewcommon...'  不能跨 view 共享同一 key
            # 简化为: 每个 view 用自己的 key, 第一个 view 写实际 key, 其余保留
            # 这里用公共 key common.dictviewcommon.total_drag
            src = src.replace(old, new)
        if src != orig:
            p.write_text(src, encoding='utf-8')
            changed += 1
    return changed

def main():
    print('--- 修复剩余 37 处硬编码中文 ---')
    funcs = [
        ('AdminEtlView', fix_admin_etl),
        ('AdminCompareView', fix_admin_compare),
        ('AdminPerfView', fix_admin_perf),
        ('AdminProductFormView', fix_admin_product_form),
        ('AdminUsersView', fix_admin_users),
        ('AdminProductsView (aria)', fix_admin_products_aria),
        ('Dict views (6)', fix_dict_template_text),
    ]
    for name, fn in funcs:
        try:
            ok = fn()
            print(f'  [{ "OK" if ok else "--" }] {name}')
        except Exception as e:
            print(f'  [ERR] {name}: {e}')

if __name__ == '__main__':
    main()
