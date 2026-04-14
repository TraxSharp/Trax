import { Link, Outlet, useLocation } from "react-router-dom";
import { getCurrentUser, getAllUsers, setApiKey } from "../lib/auth";

const NAV_ITEMS = [
  { path: "/", label: "Dashboard", icon: "📊" },
  { path: "/jobs", label: "Jobs", icon: "💼" },
  { path: "/applications", label: "Applications", icon: "📋" },
  { path: "/companies", label: "Companies", icon: "🏢" },
  { path: "/profile", label: "Profile", icon: "👤" },
];

export function Layout() {
  const location = useLocation();
  const user = getCurrentUser();

  return (
    <div className="flex h-screen bg-gray-50">
      <aside className="w-64 bg-white border-r border-gray-200 flex flex-col">
        <div className="p-6 border-b border-gray-200">
          <h1 className="text-xl font-bold text-gray-900">JobHunt</h1>
          <p className="text-sm text-gray-500 mt-1">Powered by Trax</p>
        </div>

        <nav className="flex-1 p-4 space-y-1">
          {NAV_ITEMS.map((item) => {
            const active = location.pathname === item.path;
            return (
              <Link
                key={item.path}
                to={item.path}
                className={`flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
                  active
                    ? "bg-blue-50 text-blue-700"
                    : "text-gray-700 hover:bg-gray-100"
                }`}
              >
                <span>{item.icon}</span>
                {item.label}
              </Link>
            );
          })}
        </nav>

        <div className="p-4 border-t border-gray-200">
          <a
            href="/trax"
            target="_blank"
            rel="noopener"
            className="flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-gray-500 hover:bg-gray-100"
          >
            <span>🔧</span>
            Trax Dashboard
          </a>
        </div>

        <div className="p-4 border-t border-gray-200">
          <label className="text-xs text-gray-500 block mb-1">
            Logged in as
          </label>
          <select
            value={user.key}
            onChange={(e) => setApiKey(e.target.value)}
            className="w-full text-sm border border-gray-300 rounded-md px-2 py-1"
          >
            {getAllUsers().map((u) => (
              <option key={u.key} value={u.key}>
                {u.name}
              </option>
            ))}
          </select>
        </div>
      </aside>

      <main className="flex-1 overflow-auto">
        <div className="max-w-5xl mx-auto p-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
