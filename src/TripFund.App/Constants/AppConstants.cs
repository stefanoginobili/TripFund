namespace TripFund.App.Constants;

public static class AppConstants
{
    public static class Files
    {
        public const string AppSettings = "app_settings.json";
        public const string KnownTrips = "known_trips.json";
        public const string SyncState = "sync_state.json";
        public const string TripConfig = "trip_config.json";
        public const string TransactionDetails = "transaction_details.json";
        public const string InitialImportMarker = ".initial_import";
        public const string ContentFolder = ".content";
        public const string TripFundFile = ".tripfund";
    }

    public static class Metadata
    {
        public const string Author = "author";
        public const string DeviceId = "device";
        public const string CreatedAt = "createdAt";
        public const string ContentType = "contentType";
        public const string VersioningParents = "versioning.parents";
        public const string TripSlug = "trip.slug";
        public const string SharedLink = "sharedLink";
    }

    public static class ContentTypes
    {
        public const string Trip = "tripfund/trip";
        public const string TripConfig = "tripfund/trip-config";
        public const string TransactionDetail = "tripfund/transaction-detail";
        public const string TransactionAttachment = "tripfund/transaction-attachment";
    }

    public static class MicrosoftApi
    {
        public const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
        public const string AuthUrlTemplate = "https://login.microsoftonline.com/{0}/oauth2/v2.0/authorize";
        public const string TokenUrlTemplate = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";
        public const string Scopes = "Files.ReadWrite offline_access";
        public const string SharingTokenPrefix = "u!";

        public static class Domains
        {
            public const string OneDriveShort = "1drv.ms";
            public const string OneDriveLive = "onedrive.live.com";
            public const string SharePoint = "sharepoint.com";
        }
    }
}
