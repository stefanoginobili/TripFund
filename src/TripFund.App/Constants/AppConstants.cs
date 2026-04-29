namespace TripFund.App.Constants;

public static class AppConstants
{
    public static class Folders
    {
        public const string Content = ".content";
        public const string Versions = ".versions";
        public const string Trips = "trips";
        public const string Config = "config";
        public const string Transactions = "transactions";
        public const string Details = "details";
        public const string Attachments = "attachments";
        public const string Temp = "temp";
        public const string Commits = "commits";
        public const string Sync = "sync";
        public const string Logs = "logs";
        public const string Cache = "cache";
        public const string Packages = "packages";
        public const string Devices = "devices";
        public const string Outbox = "outbox";
        public const string Inbox = "inbox";
        public const string Expanded = "expanded";
    }
    
    public static class Files
    {
        public const string AppSettings = "app_settings.json";
        public const string KnownTrips = "known_trips.json";
        public const string SyncState = "sync_state.json";
        public const string TripConfig = "trip_config.json";
        public const string TransactionDetails = "transaction_details.json";
        public const string InitialImportMarker = ".initial_import";
        public const string TripFundFile = ".tripfund";
        public const string LastSeen = ".last-seen";
        
        // Formatted filenames
        public const string ExchangeRatesTemplate = "rates_{0:yyyy_MM}.json";
        public const string RemotePackageTemplate = "pack_{0:yyyyMMddTHHmmssfffZ}_{1}.zip";
        public const string SyncLogTemplate = "sync_{0:yyyyMMddTHHmmssZ}.log";
        public const string SyncLogRotationFilePattern = "sync_*.log";
        public const string ExpenseReportTemplate = "spese_{0}.pdf";
    }

    public static class Metadata
    {
        public const string Author = "author";
        public const string DeviceId = "device";
        public const string CreatedAt = "createdAt";
        public const string ContentType = "contentType";
        public const string VersioningParents = "versioning.parents";
        public const string VersioningHead = "versioning.head";
        public const string VersioningConflict = "versioning.conflict";
        public const string TripSlug = "trip.slug";
    }

    public static class ContentTypes
    {
        public const string Trip = "tripfund/trip";
        public const string TripConfig = "tripfund/trip-config";
        public const string TransactionDetail = "tripfund/transaction-detail";
        public const string TransactionAttachment = "tripfund/transaction-attachment";
        public const string VersionedStorage = "tripfund/versioned-storage";
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

    public static class Categories
    {
        public const string DefaultName = "Spesa Generica";
        public const string DefaultIcon = "💰";
        public const string DefaultColor = "#DC3545"; // Expense theme red
        public const string UnknownIcon = "﹖";

        public static readonly Dictionary<string, (string Name, string Icon, string Color)> DefaultTripCategories = new()
        {
            { "alloggi", ("Alloggi", "⛺", "#4169E1") }, // RoyalBlue: Represents rest, stability, and the night sky.
            { "pasti", ("Pasti", "🍕", "#FF4500") }, // OrangeRed: A warm, high-energy color that stimulates appetite.
            { "trasporti", ("Trasporti", "🚌", "#8A2BE2") }, // BlueViolet: Distinct and modern, often used for logistics and movement.
            { "escursioni", ("Escursioni", "📸", "#32CD32") }, // LimeGreen: Vibrant and energetic, perfect for outdoor activities.
            { "cambusa", ("Cambusa", "🛒", "#20B2AA") }, // LightSeaGreen: A practical, utility-focused green for grocery shopping.
            { "mance", ("Mance", "🪙", "#FFD700") } // Gold: The universal color for coins, value, and gratitude.
        };
    }
}
