# Google Play Store

## Build

To build the app for the Google Play Store use the following command setting the right values for the properties.

```sh
dotnet publish src/TripFund.App/TripFund.App.csproj \
    -c Release \
    -f net10.0-android \
    /p:AndroidKeyStore=true \
    /p:AndroidSigningKeyStore=$KEYSTORE_PATH \
    /p:AndroidSigningStorePass=$KEYSTORE_PASSWORD \
    /p:AndroidSigningKeyAlias=$KEYSTORE_KEY_ALIAS \
    /p:AndroidSigningKeyPass=$KEYSTORE_PASSWORD
```

`$KEYSTORE_KEY_ALIAS` is `google-play-store` for the commonly used keystore. To list the aliases available inside a keystore you can use the following command.

```sh
keytool -list -v -keystore $KEYSTORE_PATH -alias
```

Package will be published at path

```
src/TripFund.App/bin/Release/net10.0-android/publish/com.stefanoginobili.tripfund.app-Signed.aab
```