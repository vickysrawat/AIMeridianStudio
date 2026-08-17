/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './src/**/*.{html,ts}',
  ],
  theme: {
    extend: {
      colors: {
        brand: {
          primary:   '#6366f1',
          secondary: '#8b5cf6',
          accent:    '#22d3ee',
        },
        surface: {
          DEFAULT:  '#0f172a',
          elevated: '#1e293b',
          border:   '#334155',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'BlinkMacSystemFont', '"Segoe UI"', 'sans-serif'],
        mono: ['"JetBrains Mono"', '"Fira Code"', '"Cascadia Code"', 'ui-monospace', 'monospace'],
      },
    },
  },
  plugins: [],
};
