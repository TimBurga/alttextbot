// Toggles between light and dark theme, persisting the choice to localStorage.
// Called by the theme-toggle button present on every page.
function toggleTheme() {
    var t = document.documentElement.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
    document.documentElement.setAttribute('data-theme', t);
    localStorage.setItem('altheroes_theme', t);
}
