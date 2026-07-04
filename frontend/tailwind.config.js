/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{vue,js,ts,jsx,tsx}'],
  // P2.6: 使用 class 策略驱动暗色模式 (html.dark 类切换), 配合 stores/theme.ts
  darkMode: 'class',
  // Day 9: Musk 风格 — 关闭阴影/圆角, 1px hairline 边框, 8px 网格
  theme: {
    extend: {
      colors: {
        accent: '#2563eb'   // 单一强调色 (蓝)
      },
      fontFamily: {
        sans: ['Inter', 'SF Pro', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'sans-serif']
      },
      spacing: {
        // 8px 网格
        '0.5': '4px',
        '1': '8px',
        '1.5': '12px',
        '2': '16px',
        '3': '24px',
        '4': '32px',
        '5': '40px',
        '6': '48px'
      }
    }
  },
  corePlugins: {
    // 关闭阴影 (Musk 风格无阴影)
    boxShadow: false,
    // 关闭默认圆角
    borderRadius: false
  },
  plugins: []
}
