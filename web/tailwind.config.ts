import type { Config } from "tailwindcss";

const config: Config = {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        surface: { DEFAULT: "#ffffff", soft: "#fafafa", deep: "#f5f5f4", hover: "#f4f4f4" },
        ink: { DEFAULT: "#0a0a0a", soft: "#404040", muted: "#737373", faint: "#a3a3a3" },
        line: { DEFAULT: "#eaeaea", strong: "#d4d4d4" },
        // tool/stage ramp: what kind of step the agent is taking
        step: { trend: "#1d4ed8", segment: "#0f766e", micro: "#b45309", docs: "#6d28d9", action: "#b91c1c" },
        series: { 1: "#2a78d6", 2: "#eb6834", 3: "#1baf7a", 4: "#eda100", 5: "#e87ba4", 6: "#008300" },
      },
      fontFamily: {
        sans: ["-apple-system", "BlinkMacSystemFont", "'SF Pro Text'", "'Segoe UI'", "Roboto", "Helvetica", "Arial", "sans-serif"],
        mono: ["'SFMono-Regular'", "'SF Mono'", "Menlo", "Consolas", "monospace"],
      },
    },
  },
  plugins: [],
};

export default config;
