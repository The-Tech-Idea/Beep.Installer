/**
 * Beep Installer Documentation - Navigation Manager
 * Dynamic sidebar loading + active-state management (mirrors the BeepDM Help behaviour).
 */

class NavigationManager {
    constructor() {
        this.currentPage = this.getCurrentPageName();
        this.navigationMapping = this.createNavigationMapping();
    }

    getCurrentPageName() {
        const filename = (window.location.pathname.split('/').pop() || 'index.html');
        return filename.replace('.html', '');
    }

    createNavigationMapping() {
        return {
            'index': { activeId: 'nav-home', openSection: null },
            'architecture': { activeId: 'nav-architecture', openSection: 'nav-getting-started' },
            'cli-reference': { activeId: 'nav-cli', openSection: 'nav-getting-started' },
            'authoring': { activeId: 'nav-authoring', openSection: 'nav-authoring-build' },
            'build': { activeId: 'nav-build', openSection: 'nav-authoring-build' },
            'packaging': { activeId: 'nav-packaging', openSection: 'nav-authoring-build' },
            'install-runtime': { activeId: 'nav-install-runtime', openSection: 'nav-runtime' },
            'side-by-side': { activeId: 'nav-sxs', openSection: 'nav-runtime' },
            'updates': { activeId: 'nav-updates', openSection: 'nav-app-updates' },
            'update-feed': { activeId: 'nav-update-feed', openSection: 'nav-app-updates' },
            'extensions': { activeId: 'nav-extensions', openSection: 'nav-app-updates' },
            'update-server': { activeId: 'nav-update-server', openSection: 'nav-app-updates' }
        };
    }

    getNavigationHTML() {
        return `
        <div class="logo">
            <div style="width:44px;height:44px;border-radius:12px;background:linear-gradient(135deg,#4f8cff,#7b5cff);display:flex;align-items:center;justify-content:center;color:#fff;font-size:22px;">
                <i class="bi bi-box-seam"></i>
            </div>
            <div class="logo-text">
                <h2>Beep Installer</h2>
                <span class="version">v1.0.0</span>
            </div>
        </div>

        <div class="search-container">
            <input type="text" class="search-input" placeholder="Search documentation..." onkeyup="searchDocs(this.value)">
        </div>

        <nav>
            <ul class="nav-menu">
                <li><a href="index.html" id="nav-home"><i class="bi bi-house"></i> Home</a></li>
                <li class="has-submenu" id="nav-getting-started">
                    <a href="#"><i class="bi bi-rocket"></i> Getting Started</a>
                    <ul class="submenu">
                        <li><a href="architecture.html" id="nav-architecture">Architecture</a></li>
                        <li><a href="cli-reference.html" id="nav-cli">CLI Reference</a></li>
                    </ul>
                </li>
                <li class="has-submenu" id="nav-authoring-build">
                    <a href="#"><i class="bi bi-tools"></i> Authoring &amp; Build</a>
                    <ul class="submenu">
                        <li><a href="authoring.html" id="nav-authoring">Authoring (.bsetup)</a></li>
                        <li><a href="build.html" id="nav-build">Build Pipeline</a></li>
                        <li><a href="packaging.html" id="nav-packaging">Packaging: ClickOnce &amp; MSIX</a></li>
                    </ul>
                </li>
                <li class="has-submenu" id="nav-runtime">
                    <a href="#"><i class="bi bi-gear"></i> Runtime</a>
                    <ul class="submenu">
                        <li><a href="install-runtime.html" id="nav-install-runtime">Install Pipeline</a></li>
                        <li><a href="side-by-side.html" id="nav-sxs">Side-by-Side Install</a></li>
                    </ul>
                </li>
                <li class="has-submenu" id="nav-app-updates">
                    <a href="#"><i class="bi bi-arrow-repeat"></i> App Updates</a>
                    <ul class="submenu">
                        <li><a href="updates.html" id="nav-updates">Updates Overview</a></li>
                        <li><a href="update-feed.html" id="nav-update-feed">Feed &amp; Publishing</a></li>
                        <li><a href="extensions.html" id="nav-extensions">Extensions: WinForms &amp; WPF</a></li>
                        <li><a href="update-server.html" id="nav-update-server">Update Server</a></li>
                    </ul>
                </li>
            </ul>
        </nav>
        `;
    }

    async loadNavigation() {
        const sidebar = document.getElementById('sidebar');
        if (!sidebar) return;
        sidebar.innerHTML = this.getNavigationHTML();
        this.setupNavigation();
    }

    setupNavigation() {
        this.setActiveStates();
        this.setupSubmenuToggles();
        this.setupThemeToggle();
    }

    setActiveStates() {
        const mapping = this.navigationMapping[this.currentPage];
        if (!mapping) return;
        if (mapping.activeId) document.getElementById(mapping.activeId)?.classList.add('active');
        if (mapping.openSection) document.getElementById(mapping.openSection)?.classList.add('open');
    }

    setupSubmenuToggles() {
        document.querySelectorAll('.has-submenu > a').forEach(item => {
            item.addEventListener('click', function (e) {
                e.preventDefault();
                this.parentElement.classList.toggle('open');
            });
        });
    }

    setupThemeToggle() {
        if (localStorage.getItem('theme') === 'dark') {
            document.body.setAttribute('data-theme', 'dark');
            const icon = document.getElementById('theme-icon');
            if (icon) icon.className = 'bi bi-moon-fill';
        }
    }
}

let navigationManager;
document.addEventListener('DOMContentLoaded', async function () {
    navigationManager = new NavigationManager();
    await navigationManager.loadNavigation();
});

function toggleTheme() {
    const body = document.body;
    const icon = document.getElementById('theme-icon');
    if (body.getAttribute('data-theme') === 'dark') {
        body.removeAttribute('data-theme');
        if (icon) icon.className = 'bi bi-sun-fill';
        localStorage.setItem('theme', 'light');
    } else {
        body.setAttribute('data-theme', 'dark');
        if (icon) icon.className = 'bi bi-moon-fill';
        localStorage.setItem('theme', 'dark');
    }
}

function toggleSidebar() {
    document.getElementById('sidebar')?.classList.toggle('open');
}

function searchDocs(query = '') {
    const q = query.toLowerCase();
    document.querySelectorAll('.nav-menu a').forEach(link => {
        const li = link.closest('li');
        if (!li) return;
        li.style.display = (link.textContent.toLowerCase().includes(q) || q === '') ? '' : 'none';
    });
}
