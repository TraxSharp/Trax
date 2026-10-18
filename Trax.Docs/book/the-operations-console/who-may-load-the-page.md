---
title: "8.4 Who may load the page"
description: "Chapter 8 of the Trax book, section 4: Who may load the page."
type: tutorial
area: general
parent: "8. The operations console"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/the-operations-console/open-the-namespace-to-staff-and-serve-the-console
---

# 8.4 Who may load the page

The page is the console's code, not its data, and a browser loading a page sends cookies, never
the bearer token Shopfront signs callers in with. A posture of `RequireRoles(Roles.Staff)` would
therefore refuse every load, staff included. Shopfront has no cookie sign-in, so the shell is
anonymous, `AllowAnonymousShell()`, and the gate on the data does the work: anyone can load an
empty console, and only staff get anything into it. The host logs a warning saying so on every
start, which is the point of the option.

A host with a cookie or OpenID Connect sign-in gates the page as well, with `RequirePolicy` or
`RequireRoles`. Shopfront's deployment comes back to it.

---

Next: [8.5 Open it](/docs/book/the-operations-console/open-it)
