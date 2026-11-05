import type { Metadata } from "next";
import localFont from "next/font/local";
import Header from "@/components/layout/Header";
import Footer from "@/components/layout/Footer";
import { SITE_DESCRIPTION, SITE_URL } from "@/lib/site";
import "./globals.css";

// Served from the npm packages rather than fetched from Google Fonts at build time, so a build
// needs no network beyond npm.
// Atkinson Hyperlegible Next, drawn so similar letters (I l 1, O 0) cannot be confused.
const atkinson = localFont({
  src: [
    {
      path: "../../node_modules/@fontsource-variable/atkinson-hyperlegible-next/files/atkinson-hyperlegible-next-latin-wght-normal.woff2",
      style: "normal",
    },
    {
      path: "../../node_modules/@fontsource-variable/atkinson-hyperlegible-next/files/atkinson-hyperlegible-next-latin-wght-italic.woff2",
      style: "italic",
    },
  ],
  variable: "--font-atkinson",
  weight: "200 800",
});

// The wordmark's typeface (.github/brand), for code and the monospaced labels.
const jetbrainsMono = localFont({
  src: "../../node_modules/@fontsource-variable/jetbrains-mono/files/jetbrains-mono-latin-wght-normal.woff2",
  variable: "--font-jetbrains-mono",
  weight: "100 800",
});

export const metadata: Metadata = {
  title: {
    default: "Trax .NET: the backend that checks the client's work",
    template: "%s | Trax .NET",
  },
  description: SITE_DESCRIPTION,
  metadataBase: new URL(SITE_URL),
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className="dark">
      <body
        className={`${atkinson.variable} ${jetbrainsMono.variable} font-sans antialiased`}
      >
        <div className="flex min-h-screen flex-col">
          <Header />
          {/* Each page supplies its own <main>, so the docs sidebar can sit outside it. */}
          <div className="flex-1">{children}</div>
          <Footer />
        </div>
      </body>
    </html>
  );
}
