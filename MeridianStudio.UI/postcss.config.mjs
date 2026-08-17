// PostCSS is kept here for other plugins (autoprefixer) but Tailwind
// is now run via its own CLI watcher — NOT through the Angular build.
export default {
  plugins: {
    autoprefixer: {},
  },
};
