# SmartAssistant stabilization — October 6, 2026

The exact request `Project meeting on Tuesday, October 6 at 10:00 AM` passes the real scan → AutoReplyService → reminder persistence path with mocked Gmail/Calendar and an in-memory database. With office hours 09:00–17:00 Asia/Karachi and no conflict, the stored start is 2026-10-06 05:00 UTC (10:00 PKT), status is AutoAccepted, and one reminder is created. Missing EndTime uses the existing 30-minute SlotMinutes setting. The test email arrives October 5; relative dates resolve against email receipt time so a delayed scan does not move the requested date.

## Diagnosis

- The original-slot Yes handler was enabled when either original or suggested time existed. It called approve-same-slot; the backend fell back to SuggestedStartUtc. The UI did not distinguish an explicitly created Calendar draft. Yes now requires a valid original and no suggested draft. Suggested-only records use Send reschedule email. The backend same-slot action requires the original start.
- The suggested-send backend already sets WaitingSenderConfirmation and clears ReplyRequiresApproval. This transition was preserved. Tests verify sending creates no reminder, retains the suggestion, and immediately removes the pending card.
- History/reminder/connection screens loaded during initialization or manual refresh only. Pending approvals had a separate 10-second timer without a foreground gate. The confirmed issue is stale UI. Hosted-process sleep has not been established from local source or production telemetry.
- The current start-only path already handled the exact October 6 phrase. Missing formats were tomorrow, time-before-date, and natural from/to ranges. Omitted-year and elapsed same-weekday resolution also needed future-date handling. Existing explicit range parsers remain in place. Office-hours/timezone logic was not changed to compensate for parsing.

## Files changed in this stabilization

| File | Change |
| --- | --- |
| SmartAssistant.Api/Services/AutoReply/AutoReplyService.cs | Add natural range/start-only formats and receipt-relative future dates; expose existing draft state in pending DTO; require original time for same-slot acceptance. |
| SmartAssistant.App/Components/Pages/AutoReplyApprovalBar.razor | Disable original Yes for suggested-only/draft state; replace old timer with shared foreground refresher; serialize loads; retain immediate reload after actions. |
| SmartAssistant.App/Components/Pages/ForegroundRefresh.razor | Small reusable 20-second sequential refresh component; foreground gate, disposal cancellation, linked request token. |
| SmartAssistant.App/ForegroundState.cs | Shared foreground flag, no background worker. |
| SmartAssistant.App/App.xaml.cs | Update foreground flag on app sleep/resume/start and window activation/deactivation. |
| SmartAssistant.App/Components/Pages/ProcessedEmailHistory.razor | Invoke existing history GET loader every 20 seconds while foreground; link GET cancellation. |
| SmartAssistant.App/Components/Pages/Reminder.razor | Same refresh mechanism for existing reminder GET loader. |
| SmartAssistant.App/Components/Pages/ReminderHistory.razor | Same refresh mechanism for existing reminder-history GET loader. |
| SmartAssistant.App/Components/Pages/GoogleAuth.razor | Refresh existing connection-status GET; guard overlapping status loads and link cancellation. |
| SmartAssistant.App/Components/Pages/Gmail.razor | Same connection-status refresh/guard; existing connection/navigation behavior retained. |
| tests/SchedulingChecks/Program.cs | Requested natural expressions, future-year resolution, exact 9–17 October 6 end-to-end regression. |
| tests/SchedulingChecks/HistoryApprovalChecks.cs | Draft/original controls, immediate card removal, waiting state, unparseable reasons, real 20-second mounted-component refresh, foreground gate and cancellation. |
| tests/SchedulingChecks/SchedulingChecks.csproj | Link actual shared refresh component and foreground flag into existing test harness; no package changes. |
| tests/SchedulingChecks/STABILIZATION-2026-10-06.md | This report. |

Backend changes are limited to parsing, pending DTO state, and removal of suggested-time fallback from original-slot acceptance. Automatic office-hour/conflict gates and suggested-send/external-confirmation business flow remain intact.

## Validation and limits

- API/Core and Razor regression harness build succeeded; all 159 checks passed without production database access or real Gmail/Calendar calls.
- Actual 20-second timer loaded a newly added history row while its component stayed mounted. Background gate prevented refresh; disposal canceled its token. No processing POST was made by polling.
- Android MAUI build succeeded, zero errors. Existing dependency-resolution/obsolete API warnings remain. API build retained existing dependency vulnerability warnings; package versions were not changed.
- Physical-device and deployed-host behavior were not exercised. No deployment, real emails, schema, authentication, hosting, API URL, target framework or package changes were made.
- Hangfire remains `*/10 * * * *`. UI polling does not trigger email scans or change their cadence. Newly sent email may still wait for the next existing scan; after backend processing, an active screen refreshes within about 20 seconds plus request latency.
- Existing 14-day eligibility and deduplication remain unchanged. Already processed/pending messages are not forcibly reprocessed by refresh.
- Foreground requests provide normal API traffic during a demo, but cannot guarantee a free host will never sleep or suspend background jobs. Hosting sleep remains unverified and hosting configuration is untouched.
