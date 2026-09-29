import { test, expect } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://127.0.0.1:5175'

const auth = {
  token: 'eyJhbGciOiJub25lIn0.eyJzdWIiOiJ0ZXN0LWFkbWluIiwicm9sZSI6ImFkbWluIn0.test',
  refreshToken: 'rehearsal-refresh-token',
  user: { username: 'rehearsal-admin', role: 'admin' },
  expiresAt: Date.now() + 3600_000
}

test.describe('OEM 锚点目录后台页面', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript((value) => {
      localStorage.setItem('sakura_admin_auth', JSON.stringify(value))
    }, auth)

    await page.route('**/api/admin/oem-catalog/**', async (route) => {
      const url = new URL(route.request().url())
      const path = url.pathname
      if (path.endsWith('/summary')) {
        await route.fulfill({ json: {
          lastPublishedBatchId: 1,
          oemProductCount: 49391,
          productWithSpecCount: 10020,
          crossReferenceCount: 529499,
          machineApplicationCount: 709843,
          activeMr1MappingCount: 0
        } })
        return
      }
      if (path.endsWith('/products') && route.request().method() === 'GET') {
        await route.fulfill({ json: {
          page: 1, pageSize: 50, total: 49391,
          items: [{
            oemNo1: 'SH 56212', oemNo1Display: 'SH 56212',
            productNameCandidates: ['Hydraulic Filter'], specConflictFieldCount: 0,
            sourceBatchId: 1, updatedAt: '2026-09-28T00:00:00Z',
            crossReferenceCount: 281, machineApplicationCount: 10515, activeMr1: null
          }]
        } })
        return
      }
      if (path.includes('/products/xrefs/')) {
        await route.fulfill({ json: {
          page: Number(url.searchParams.get('page') || 1), pageSize: 20, total: 281,
          items: [{ productName1: 'Hydraulic Filter', oemBrand: 'MANN', oemNo3: 'HF-001', sourceRowNos: [738086, 738087], mergedSourceRowCount: 2 }]
        } })
        return
      }
      if (path.includes('/products/applications/')) {
        await route.fulfill({ json: {
          page: Number(url.searchParams.get('page') || 1), pageSize: 20, total: 10515,
          items: [{ machineBrand: 'ALEXANDER DENNIS LIMITED', machineModel: 'ENVIRO 200 H', modelName: 'Bus', engineBrand: 'Cummins', engineModel: 'ISBE5-207', productionDate: '2017-02-01>', power: '152', sourceRowNo: 738086 }]
        } })
        return
      }
      if (path.endsWith('/SH%2056212') || decodeURIComponent(path).endsWith('/SH 56212')) {
        await route.fulfill({ json: {
          oemNo1: 'SH 56212', oemNo1Display: 'SH 56212',
          productNameCandidates: ['Hydraulic Filter'], specPayload: { length: '500', width: '200' },
          specConflictFieldCount: 0, sourceBatchId: 1,
          createdAt: '2026-09-28T00:00:00Z', updatedAt: '2026-09-28T00:00:00Z',
          crossReferenceCount: 281, machineApplicationCount: 10515, activeMr1: null
        } })
        return
      }
      await route.continue()
    })
  })

  test('搜索、详情标签和分页明细正常绑定', async ({ page }) => {
    await page.goto(`${BASE}/admin/oem-catalog`, { waitUntil: 'domcontentloaded' })
    await expect(page.getByTestId('oem-catalog-page')).toBeVisible()
    await expect(page.locator('.font-mono.text-base').first()).toHaveText(/49,391|49391/)
    await expect(page.getByText('SH 56212')).toBeVisible()

    await page.getByText('SH 56212').last().click()
    await expect(page.getByText('OEM 目录详情', { exact: true })).toBeVisible()
    await expect(page.getByText('HF-001')).toBeVisible()
    await expect(page.getByText('合并行数')).toBeVisible()

    await page.getByRole('tab', { name: '机型适配' }).click()
    await expect(page.getByText('ENVIRO 200 H')).toBeVisible()
    await expect(page.getByLabel('OEM 目录详情').getByText(/10,515|10515/).first()).toBeVisible()
    await expect(page.getByText('MR.1').last()).toBeVisible()
  })
})
