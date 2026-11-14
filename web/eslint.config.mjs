import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";
import datatableListRoot from "./eslint-rules/datatable-list-root.mjs";
import preferCrudForm from "./eslint-rules/prefer-crud-form.mjs";
import noCrossModuleImport from "./eslint-rules/no-cross-module-import.mjs";
import iconButtonLabel from "./eslint-rules/icon-button-label.mjs";
import dialogMaxWidthPrefix from "./eslint-rules/dialog-max-width-prefix.mjs";
import noNativeDialogControls from "./eslint-rules/no-native-dialog-controls.mjs";
import noPaletteClasses from "./eslint-rules/no-palette-classes.mjs";
import focusRingToken from "./eslint-rules/focus-ring-token.mjs";
import pageHeaderActions from "./eslint-rules/page-header-actions.mjs";
import singlePrimaryPerRegion from "./eslint-rules/single-primary-per-region.mjs";
import aboveTableBudget from "./eslint-rules/above-table-budget.mjs";

const swapmid = {
  rules: {
    ...datatableListRoot.rules,
    ...preferCrudForm.rules,
    ...noCrossModuleImport.rules,
    ...iconButtonLabel.rules,
    ...dialogMaxWidthPrefix.rules,
    ...noNativeDialogControls.rules,
    ...noPaletteClasses.rules,
    ...focusRingToken.rules,
    ...pageHeaderActions.rules,
    ...singlePrimaryPerRegion.rules,
    ...aboveTableBudget.rules,
  },
};

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    files: ["src/**/*.{ts,tsx}"],
    plugins: { swapmid },
  },
  {
    files: ["src/**/*.tsx"],
    rules: {
      "swapmid/datatable-list-root": "error",
      "swapmid/prefer-crud-form": "error",
      "swapmid/icon-button-label": "error",
      "swapmid/dialog-max-width-prefix": "error",
    },
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    rules: {
      "swapmid/no-cross-module-import": "error",
    },
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    ignores: ["**/*.{test,spec}.{ts,tsx}"],
    rules: {
      "swapmid/no-native-dialog-controls": "error",
      "swapmid/no-palette-classes": "error",
      "swapmid/focus-ring-token": "error",
    },
  },
  {
    files: ["src/**/*.tsx"],
    ignores: ["**/*.{test,spec}.tsx"],
    rules: {
      "swapmid/page-header-actions": "error",
      "swapmid/single-primary-per-region": "error",
      "swapmid/above-table-budget": "error",
    },
  },
  {
    files: [
      "src/components/ui/**",
      "src/app/(dashboard)/docs/api-reference/**",
    ],
    rules: {
      "swapmid/no-palette-classes": "off",
    },
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    ignores: [
      "**/api/generated/**",
      "**/*.gen.ts",
      "**/docs/api-reference/api-doc-overrides.ts",
      "**/docs/api-reference/integration-guides.ts",
    ],
    rules: {
      "max-lines": ["warn", { max: 500, skipBlankLines: true, skipComments: true }],
    },
  },
  {
    files: ["src/**/*.{test,spec}.{ts,tsx}", "vitest.setup.ts"],
    rules: {
      "react-hooks/rules-of-hooks": "off",
      "max-lines": "off",
    },
  },
  globalIgnores([
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
]);

export default eslintConfig;
