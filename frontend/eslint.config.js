// Day 14+: ESLint flat config (ESLint 9+)
// WHY 配置:
//   1. no-unexpected-multiline: 防止类似 xrefSummary/machineSummary 三元
//      表达式缺闭括号 → 整页 500 的回归 (Day 14 PublicCompareView bug 根因)
//   2. vue/vue-recommended: 模板 + script 基础规则
//   3. 渐进式引入: 严格规则设为 warn 而非 error, 避免新 lint 引入即 CI 红
//   4. 关键错误规则: no-unexpected-multiline / no-unreachable 强制 error
//
// 用法:
//   npm run lint        # 扫描整个 src
//   npm run lint:fix    # 自动修复
//   npm run lint:critical  # 只跑核心防退化规则 (CI 用, 失败即拦截)
import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import vue from 'eslint-plugin-vue'
import vueParser from 'vue-eslint-parser'

// 项目使用单 tsconfig.json (无 tsconfig.app.json), 显式指向
const tsconfigPath = './tsconfig.json'

// ===== 防退化核心规则 (CI 必须通过) =====
//   只列最关键的几条: 任一命中即视为代码异味
export const CRITICAL_RULES = [
  'no-unexpected-multiline',  // 缺闭括号 → 整页 500
  'no-unreachable',          // ASI 误吞
  'no-eval',                 // 性能 + 安全
  'no-implicit-globals',     // 全局污染
  'vue/no-template-shadow',  // 性能
  'vue/no-v-html'            // XSS
]

export default tseslint.config(
  // 忽略文件
  {
    ignores: [
      'dist/**',
      'node_modules/**',
      'src/api/generated-types.ts',
      'src/**/*.test.ts',
      'src/**/__tests__/**'
    ]
  },

  // 全局基础规则 (标准 + 风格)
  js.configs.recommended,
  ...tseslint.configs.recommended,

  // Vue 规则
  ...vue.configs['flat/recommended'],
  {
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
        project: tsconfigPath,
        tsconfigRootDir: import.meta.dirname,
        extraFileExtensions: ['.vue']
      }
    },
    files: ['**/*.vue'],
    rules: {
      // 核心: 防止三元表达式缺闭括号 (Day 14 PublicCompareView xrefSummary bug 根因)
      'no-unexpected-multiline': 'error',
      'no-unreachable': 'error',
      'no-eval': 'error',
      'vue/no-v-html': 'warn',
      // Vue 模板规则
      'vue/multi-word-component-names': 'off',  // 允许单字组件名 (Login, Search)
      'vue/require-default-prop': 'off',  // 配合 withDefaults 即可
      'vue/html-self-closing': 'off',  // 风格问题不强求
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/html-indent': 'off',
      'vue/html-closing-bracket-newline': 'off',
      'vue/first-attribute-linebreak': 'off',
      'vue/attributes-order': 'off',
      'vue/attribute-hyphenation': 'off',
      'vue/v-on-event-hyphenation': 'off'
    }
  },

  // 严格 TS 规则 (仅对 .ts)
  {
    files: ['**/*.ts'],
    languageOptions: {
      parserOptions: {
        project: tsconfigPath,
        tsconfigRootDir: import.meta.dirname
      }
    },
    rules: {
      'no-unexpected-multiline': 'error',
      'no-unreachable': 'error',
      '@typescript-eslint/no-unused-vars': ['warn', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_'
      }],
      '@typescript-eslint/no-explicit-any': 'off',  // 历史代码 any 多, 不强求
      '@typescript-eslint/no-unsafe-return': 'off',
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/consistent-type-imports': 'off',
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/no-floating-promises': 'off',
      '@typescript-eslint/await-thenable': 'off',
      '@typescript-eslint/no-misused-promises': 'off'
    }
  },

  // 测试文件宽松
  {
    files: ['**/*.test.ts', 'tests/**/*'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-non-null-assertion': 'off'
    }
  }
)