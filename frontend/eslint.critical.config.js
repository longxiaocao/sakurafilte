// 关键规则 lint 配置 (CI 用, 失败即拦截)
//   只启用防退化的核心规则:
//     - no-unexpected-multiline: 防 xrefSummary 类缺闭括号 bug
//     - no-unreachable: 防 return 后 ASI 误吞
//     - no-eval: 性能 + 安全
//     - no-implicit-globals: 防全局污染
//     - vue/no-v-html: XSS 防护
//
// 用法: npm run lint:critical
import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import vue from 'eslint-plugin-vue'
import vueParser from 'vue-eslint-parser'

const tsconfigPath = './tsconfig.json'

export default tseslint.config(
  { ignores: ['dist/**', 'node_modules/**', 'src/api/generated-types.ts'] },
  js.configs.recommended,
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
    files: ['**/*.{ts,vue}'],
    rules: {
      // 关闭所有非关键规则
      'no-unused-vars': 'off',
      'no-undef': 'off',  // Vue 模板宏 <script setup> 在 no-undef 下会误报
      'no-empty': 'off',
      'no-unused-private-class-members': 'off',
      'no-prototype-builtins': 'off',
      'no-useless-escape': 'off',
      'no-constant-condition': 'off',
      'no-useless-catch': 'off',
      'no-self-assign': 'off',
      'no-self-compare': 'off',
      'no-cond-assign': 'off',
      'no-dupe-else-if': 'off',
      'no-duplicate-case': 'off',
      'no-fallthrough': 'off',
      'no-loss-of-precision': 'off',
      'no-misleading-character-class': 'off',
      'no-redeclare': 'off',
      'no-sparse-arrays': 'off',
      'no-template-curly-in-string': 'off',
      'no-irregular-whitespace': 'off',
      'no-async-promise-executor': 'off',
      'no-await-in-loop': 'off',
      'no-console': 'off',
      'no-debugger': 'off',
      'no-ex-assign': 'off',
      'no-extra-boolean-cast': 'off',
      'no-func-assign': 'off',
      'no-invalid-regexp': 'off',
      'no-new-wrappers': 'off',
      'no-octal': 'off',
      'no-unused-labels': 'off',
      'no-useless-backreference': 'off',
      'no-useless-call': 'off',
      'no-useless-concat': 'off',
      'no-useless-rename': 'off',
      'no-useless-return': 'off',
      'no-var': 'off',
      'no-with': 'off',
      // 关闭所有 vue 规则, 只保留 critical
      'vue/multi-word-component-names': 'off',
      'vue/no-v-html': 'error',
      'vue/no-unused-vars': 'off',
      'vue/no-unused-components': 'off',
      'vue/no-mutating-props': 'off',
      'vue/no-parsing-error': 'off',  // parser 错误已由 TS 拦截, 这里不重复
      'vue/no-template-shadow': 'off',
      'vue/require-default-prop': 'off',
      'vue/attribute-hyphenation': 'off',
      'vue/v-on-event-hyphenation': 'off',
      'vue/html-self-closing': 'off',
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/multiline-html-element-content-newline': 'off',
      'vue/html-indent': 'off',
      'vue/html-closing-bracket-newline': 'off',
      'vue/first-attribute-linebreak': 'off',
      'vue/attributes-order': 'off',
      'vue/no-async-in-computed-properties': 'off',
      'vue/no-side-effects-in-computed-properties': 'off',
      'vue/no-empty-component-block': 'off',
      'vue/require-prop-type': 'off',
      'vue/return-in-computed-property': 'off',
      'vue/use-v-on-exact': 'off',
      'vue/valid-template-root': 'off',
      'vue/valid-v-slot': 'off',
      'vue/valid-v-for': 'off',
      'vue/valid-v-bind': 'off',
      'vue/valid-v-model': 'off',
      'vue/valid-v-on': 'off',
      'vue/valid-v-once': 'off',
      'vue/comment-directive': 'off',
      'vue/no-bare-strings-in-template': 'off',
      'vue/no-child-content': 'off',
      'vue/no-constant-condition': 'off',
      'vue/no-raw-text': 'off',
      'vue/no-restricted-syntax': 'off',
      'vue/no-setup-props-destructure': 'off',
      'vue/no-spaces-around-equal-signs-in-attribute': 'off',
      'vue/no-unregistered-components': 'off',
      'vue/no-unsupported-features': 'off',
      'vue/no-useless-mustaches': 'off',
      'vue/no-useless-v-bind': 'off',
      'vue/no-v-text': 'off',
      'vue/prefer-import-from-vue': 'off',
      'vue/prefer-true-attribute-shorthand': 'off',
      'vue/v-on-function-call': 'off',
      '@typescript-eslint/no-unused-vars': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-unsafe-return': 'off',
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/no-unsafe-call': 'off',
      '@typescript-eslint/no-unsafe-argument': 'off',
      '@typescript-eslint/no-empty-object-type': 'off',
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/consistent-type-imports': 'off',
      '@typescript-eslint/ban-ts-comment': 'off',
      '@typescript-eslint/no-unused-expressions': 'off',
      '@typescript-eslint/no-this-alias': 'off',
      '@typescript-eslint/no-var-requires': 'off',
      '@typescript-eslint/no-require-imports': 'off',
      '@typescript-eslint/triple-slash-reference': 'off',
      '@typescript-eslint/no-unnecessary-type-assertion': 'off',
      '@typescript-eslint/no-redundant-jsdoc': 'off',
      '@typescript-eslint/no-base-to-string': 'off',
      '@typescript-eslint/restrict-template-expressions': 'off',
      '@typescript-eslint/restrict-plus-operands': 'off',
      '@typescript-eslint/no-unsafe-enum-comparison': 'off',
      '@typescript-eslint/no-misused-promises': 'off',
      '@typescript-eslint/no-floating-promises': 'off',
      '@typescript-eslint/await-thenable': 'off',
      '@typescript-eslint/no-for-in-array': 'off',
      '@typescript-eslint/no-implied-eval': 'off',
      '@typescript-eslint/no-throw-literal': 'off',
      '@typescript-eslint/prefer-as-const': 'off',
      '@typescript-eslint/prefer-nullish-coalescing': 'off',
      '@typescript-eslint/prefer-optional-chain': 'off',
      '@typescript-eslint/unbound-method': 'off',

      // ===== 关键防退化规则 (只这几条必须 error) =====
      'no-unexpected-multiline': 'error',
      'no-unreachable': 'error',
      'no-eval': 'error',
      'no-implicit-globals': 'error'
    }
  }
)