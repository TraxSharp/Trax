---
title: Recovery tests
description: "What the Recovery sample's E2E and browser test suites prove, test by test, and what each suite needs to run."
type: explanation
area: samples
parent: Recovery
nav_order: 3
---

# Recovery tests

Part of the [Recovery](/docs/samples/recovery) sample tour.

## The E2E tests

`Trax.Samples.Recovery.E2E` boots the real host with `WebApplicationFactory` against the
`recovery_e2e_tests` database (port 5432, or `TRAX_TEST_PG_PORT`), with a counting decider in place
of the demo one, and asserts over GraphQL. `RestartTests` starts the host as two processes of its own, one after
the other, on the `recovery_restart_e2e_tests` database, so it can kill the first the way a crash does:

| Test | Proves |
|---|---|
| `RefundRun_PaymentTimedOutOnce_RetryTakesTheSameTrackWithoutAskingAgain` | The retry runs the whole chain and replays both answers, `Problem` and `ApproveRefund`; the state hash is keyed (`k1:`) |
| `RefundRun_CrashedWhileEmailing_RetryStartsFromTheCheckpoint` | Attempt 2 resumes after the checkpoint and completes; its junction events hold only `NotifyCustomer` and its journal no decision; the decider was asked each question once across both attempts |
| `A_crash_in_NotifyCustomer_resumes_from_RefundOutcome_without_paying_again` | The failed run's graph offers `NotifyCustomer#0` and not `IssuePayment`; `resumeExecution` and the dashboard's resume each run only `NotifyCustomer`, return the same result and draw the steps before as `RESTORED`; a completed resume is not resumed again; `TraxInvariants` finds nothing new |
| `RefundRun_OnOtherTracks_CrashesOnce_AndTheRetryReplaysTheDecisions` | Orders A-1002 (`TraceParcel`, review) and A-1003 (`CheckReturnWindow`, decline) crash once on their track, and the retry replays both answers |
| `RunWithNoCrashArmed_CompletesInOneAttempt` | One attempt, and the once manifest disables itself |
| `TriggerWithAskAfresh_DuringTheBackoff_RetryAsksTheModelAgain` | `triggerManifest(askAfresh: true)` makes a refund's retry ask both questions again |
| `RequeueExecution_ReplaysByDefault_AndAsksAgainWithAskAfresh` | `requeueExecution` replays; with `askAfresh: true` it asks again |
| `DataChangedDuringTheBackoff_RetryAsksAfresh_AndTakesTheTrackTheNewDataCallsFor` | The retry stays linked, refuses both answers for a changed state, asks afresh and takes `Unsure` to `QueueForReview` |
| `ViewerSubscriber_SeesTheShape_ButNotTheAnswersOrTheTrack` | The broadcast view gets no answers, and every step on a track is `(withheld)` |
| `TheSchedulersOwnRuns_AreSweptAfterTheirRetention` | The scheduler's own runs are deleted once past their retention |
| `Seeding_Twice_AddsNoWorks` | Seeding the corpus again adds nothing |
| `Run_OverTheWholeCorpus_MapsTopicsAndFindsTheHiddenTwins` | The map of the seeded corpus: 32 pairs written, the strongest pair, and exactly the two hidden twins |
| `Run_OverASlice_TakesTheCoCitationTrackTheModelChooses` | The three slices take the gate's `Yes`, `Unsure` and `No` tracks |
| `CrashedCoCitationBranch_FailsTheRunNamingIt_AndTheJoinWritesNothing` | The run throws a `BranchesFailedException` naming `Parallel#0/cocitation`, and no pair is written |
| `ScheduledRun_CrashedOnce_FailsNamingTheBranch_ThenEveryBranchRecordsItsSteps` | Attempt 1 fails naming the branch with nothing written; the retry replays the branch's answer, writes the pairs, records steps under all three branches, and `runGraph` draws them |
| `Discovery_StartsOneInstancePerPartition_AndARerunStartsNoneAndQueuesNothing` | Six partitions, six instances, six ingest runs; a second discovery starts none and queues nothing |
| `EachPartition_IsIngested_AndTheUnsureOne_GoesToReview` | Five instances reach `Ingested` and Crossref/2025-02 reaches `NeedsReview`, each holding counts and a fingerprint; `Approve` moves it on, and `Retry` from `Ingested` is a `no-transition` |
| `ACrashedIngest_Fails_AndARetry_RunsItAgainUnderANewToken` | A crashed ingest reaches `Failed` and is not retried by the scheduler; `Retry` queues a new run under a new token and the instance reaches `Ingested`, and a late delivery of either run's outcome moves nothing |
| `OnlyAnOperator_MayActOnAPartition` | The viewer key's `partitionAction` is refused with `TRAX_AUTHORIZATION`, and the instance neither moves nor queues a run |
| `AnOperator_CannotCancelTheBuild_ThroughTheRunOrItsEntry_OneByOneOrInBulk` | `cancelWorkQueueEntry`, `cancelExecution` and `cancelExecutions` refuse or skip the run a user's draft started, with one message, and the build still reaches `Built` |
| `Operators_SeeThePartitions_ReadOnly_AndNoUserReachesThem` | `operations.machineInstances` lists the system instances and has no context field; the system-owned machine has no field under `machines`, and no generic read is served |
| `TheWizard_BuildsTheMap_AndTheDraftKeepsAPointerToIt` | Fields, years, build: the run's outcome moves the draft to `Built` with the map's id and summary, the pairs are under that id, and a reload returns the same snapshot |
| `TheServerMintsTheDraftId_AndADiscardedDraftIsGone` | `start` mints the id and builds the first step; a discarded draft reads as no draft |
| `StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNothingThatCountsTowardTheCap` | From `Built`, `StartOver` resets the draft to the first step, clears its due time, and leaves a draft that discards |
| `AnAutosaveIntoBuilt_IsRefused`, `WhileBuilding_AHandFiredOutcome_AndAnAutosave_AreRefused` | neither a save nor the outcome is served to a client; the real outcome still lands |
| `CancellingTheBuild_GoesBackToTheRange_AndCancelsItsRun`, `AFailedBuild_IsRebuiltByEnteringBuildingAgain_WithANewRun` | Leaving `Building` cancels its run; a rebuild is a new run |
| `ARebuiltMap_LeavesTheOldMapToTheSweep_AndKeepsTheOneTheDraftPointsAt`, `TheSweep_NeverDeletesAMapAStoredDraftPointsAt_NorOneItSawOnlyOnce` | After a rebuild, two sweeps delete the replaced map's pairs and keep the new one's; a map is deleted only on the second sweep that finds it unreferenced |
| `AnOrder_IsDelivered_AndTheServerPricesTheCart`, `PlaceOrderPressedTwice_MakesOneOrderAndOneRun` | The order runs all five steps and reaches `Delivered`; one request sent twice is answered once, a second request is `no-transition`, and the order has one run |
| `ATabletThatTimesOut_IsTriedAgain_AndTheOrderIsDelivered`, `AClosedRestaurant_CancelsTheOrder_BeforeTheCardIsCharged`, `ACartNobodyOrders_IsClearedByTheServer` | The tablet's second attempt is answered inside the one run; a closed restaurant fails the run before `ChargeCard` and the order is `Cancelled`; the cart's timer moves it to `Cleared` |
| `AHostKilledMidBuild_FailsTheRunOnTheNextStart_AndTheDraftIsRebuiltFromBuildFailed` | A host process killed mid-build leaves the run `InProgress`; the next host fails it on startup, the draft reaches `BuildFailed`, the seeded data is there, and a rebuild reaches `Built` |

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.E2E
```

## The browser tests

`Trax.Samples.Recovery.Browser` drives the built page in chromium with Playwright for .NET
([ADR 0049](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0049-browser-tests-use-playwright-for-dotnet-in-test-projects-only.md)),
against the host started as a process on the `recovery_browser_tests` database. It builds the page with vite, serves
it at the origin the host's CORS policy admits, and forwards the page's requests and its socket to the host, so the
page's code runs unchanged and every request it makes is seen. Each test waits on a locator, a request or the
database, never a delay, and `TraxInvariants` is checked after every one:

| Test | Proves |
|---|---|
| `TheWizard_RunsEndToEnd_WithAdHocDocumentsRefusedAndGenericMutationsOff` | Fields, years, build to `Built` through the typed operations only; an ad-hoc document is refused, and no `stateMachine` field exists |
| `TheBuildOutcome_ArrivesByPush_WithNoPollingRequest` | The page makes no request between the `build` answer and rendering `Built` |
| `TheReviewTimer_ExpiresByPush` | A due review timer moves the draft to `Expired` on the server, and the page shows it with no request of its own |
| `AViewerRolesOperatorOnlyEvent_IsRefusedOnTheServer` | The viewer key's `build` is `forbidden`, and the draft's revision does not move |
| `TheDashboardAndGraphQL_ShowTheSameStateRevisionAndRuns` | `get`, the stored row and the dashboard's instance page agree on the state and revision, and on no live run |
| `ReloadingTheTab_ResumesTheServerMintedInstance` | A reload opens the kept draft at its step with one `start` in all and no save |
| `TwoTabs_SeeEachOthersTransitionsInRevisionOrder` | A step in one tab reaches the other by push, in order |
| `ADraftTheServerNoLongerHolds_IsForgotten_AndANewOneStarted` | A draft discarded elsewhere is pushed as `gone`; the page starts a new one, and a reload keeps that one |
| `StartingANewMap_DiscardsTheOldDraft` | **Start a new map** before a build discards the old draft before starting the next |
| `StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNoneBehind` | **Start a new map** from `Built` resets the same draft with `StartOver`, with one start request in all and the review timer gone |
| `ADeployWithANewIR_ShowsClientOutdated` | A page built for another IR shows `client-outdated`, hides the wizard and makes no further request |
| `KillingTheHostMidBuild_RecoversThroughTheStuckRunPath_AndThePageFollows` | The next host fails the dead build and the page follows to `BuildFailed` by push, with no request of its own |

They need the page's npm packages (`npm ci` in `Trax.Samples.Recovery.Client` and in `Trax.Api.StateMachine`) and
chromium (`pwsh tests/Trax.Samples.Recovery.Browser/bin/Debug/net10.0/playwright.ps1 install chromium` after a
build):

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.Browser
```

## SDK Reference

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Queries](/docs/sdk-reference/graphql-api/queries)
