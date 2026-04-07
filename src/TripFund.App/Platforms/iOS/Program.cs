using ObjCRuntime;
using UIKit;
using Foundation;

namespace TripFund.App;

public class Program
{
	// This is the main entry point of the application.
	static void Main(string[] args)
	{
		// Force it-IT locale for system pickers and WebView native controls
		NSUserDefaults.StandardUserDefaults.SetValueForKey(NSArray.FromNSObjects(new NSString("it")), new NSString("AppleLanguages"));
		NSUserDefaults.StandardUserDefaults.Synchronize();

		// if you want to use a different Application Delegate class from "AppDelegate"
		// you can specify it here.
		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
