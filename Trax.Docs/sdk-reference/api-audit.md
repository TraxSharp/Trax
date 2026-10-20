---
title: API Audit
description: "Index of the Trax.Api.GraphQL.Audit reference: AddAudit, TraxAuditEntry, TraxAuditOptions, TraxAuditChannel, ITraxAuditSink and ITraxAuditRedactor."
type: landing
area: security
parent: SDK Reference
has_children: true
nav_order: 9
---

# API Audit

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Reference for the request-level audit pipeline of a Trax GraphQL host. How it fits together is in [Audit pipeline](/docs/security/secure-a-graphql-host#3-write-the-audit-sink).

## Trax.Api.GraphQL.Audit

- [AddAudit](/docs/sdk-reference/api-audit/add-audit): registers the pipeline on `TraxGraphQLBuilder`.
- [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry): the record of one audited request.
- [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink): the destination you implement.
- [ITraxAuditRedactor](/docs/sdk-reference/api-audit/i-trax-audit-redactor): which variables an entry records; `DefaultAuditRedactor` records none.
- [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options): capacity, batching, truncation, retries.
- [TraxAuditChannel](/docs/sdk-reference/api-audit/trax-audit-channel): the queue, its meter and the dropped-entry counter.
