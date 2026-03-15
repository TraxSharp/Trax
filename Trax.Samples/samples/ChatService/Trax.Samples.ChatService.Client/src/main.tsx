import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { UserProvider } from "./context/UserContext";
import { App } from "./components/App";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <UserProvider>
      <App />
    </UserProvider>
  </StrictMode>,
);
