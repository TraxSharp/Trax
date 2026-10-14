---
title: AddMediator
description: "Where AddMediator is documented: its full reference page under Configuration, and the lower-level AddServiceTrainBus registration it performs."
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 12
package: Trax.Mediator
namespace: Trax.Mediator.Extensions
source:
  - Trax.Mediator/src/Trax.Mediator/Extensions/ServiceExtensions.cs
---

# AddMediator

`AddMediator` registers the mediator on the Trax builder, after `AddEffects`. Its overloads, the
`TraxMediatorBuilder` settings and what it registers are on
[Configuration: AddMediator](/docs/sdk-reference/configuration/add-mediator).

## See also

- [AddMediator](/docs/sdk-reference/configuration/add-mediator), the full reference page
- [AddServiceTrainBus](/docs/sdk-reference/mediator-api/add-service-train-bus), the registration `AddMediator` performs
- [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration), the settings it resolves
