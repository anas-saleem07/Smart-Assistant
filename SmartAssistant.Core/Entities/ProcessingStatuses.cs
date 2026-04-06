namespace SmartAssistant.Core.Entities
{
    public static class ProcessingStatuses
    {
        public const string ApprovalPending = "ApprovalPending";
        public const string ReschedulePending = "ReschedulePending";
        public const string WaitingSenderConfirmation = "WaitingSenderConfirmation";
        public const string Confirmed = "Confirmed";
        public const string RejectedBySender = "RejectedBySender";
        public const string RejectedByUser = "RejectedByUser";
        public const string Error = "Error";
        public const string AutoAccepted = "AutoAccepted";
    }
}