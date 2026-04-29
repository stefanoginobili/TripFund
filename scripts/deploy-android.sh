#!/bin/bash

# Configuration
ANDROID_SDK_PATH="$HOME/Library/Android/sdk"
ADB_PATH="$ANDROID_SDK_PATH/platform-tools/adb"
PROJECT_PATH="src/TripFund.App/TripFund.App.csproj"
PACKAGE_NAME="com.stefanoginobili.tripfund.app.dev"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

printf "${BLUE}--- TripFund Android Deployment Tool ---${NC}\n"

# 1. Get Running Devices
printf "${YELLOW}Searching for connected devices...${NC}\n"
RUNNING_DEVICES=$($ADB_PATH devices | awk 'NF==2 && $2=="device" {print $1}')

if [ -z "$RUNNING_DEVICES" ]; then
    printf "${RED}[ERROR] No online devices or emulators found.${NC}\n"
    exit 1
fi

# 2. Build Menu
OPTIONS=()
SERIALS=()

for dev in $RUNNING_DEVICES; do
    MANUFACTURER=$($ADB_PATH -s "$dev" shell getprop ro.product.manufacturer | tr -d '\r')
    MODEL=$($ADB_PATH -s "$dev" shell getprop ro.product.model | tr -d '\r')
    
    if [[ "$dev" == *"emulator"* ]]; then
        AVD_NAME=$($ADB_PATH -s "$dev" emu avd name 2>/dev/null | head -n 1 | tr -d '\r')
        if [ -z "$AVD_NAME" ]; then AVD_NAME="$MODEL"; fi
        LABEL="[Emulator] $AVD_NAME"
    else
        LABEL="[Physical] $MANUFACTURER $MODEL"
    fi
    
    OPTIONS+=("$LABEL ($dev)")
    SERIALS+=("$dev")
done

# 3. Present Menu
if [ ${#OPTIONS[@]} -eq 1 ]; then
    SELECTED_SERIAL="${SERIALS[0]}"
    printf "Auto-selecting device: ${GREEN}${OPTIONS[0]}${NC}\n"
else
    printf "Select a target device:\n"
    for j in "${!OPTIONS[@]}"; do
        printf "  $((j+1))) ${OPTIONS[$j]}\n"
    done

    printf "Enter choice (1-${#OPTIONS[@]}): "
    read CHOICE
    INDEX=$((CHOICE-1))

    if [[ $INDEX -lt 0 || $INDEX -ge ${#OPTIONS[@]} ]]; then
        printf "${RED}[ERROR] Invalid selection.${NC}\n"
        exit 1
    fi
    SELECTED_SERIAL="${SERIALS[$INDEX]}"
fi

# 4. Execute Build and Deploy
printf "\n${BLUE}Building and deploying to $SELECTED_SERIAL...${NC}\n"
dotnet build "$PROJECT_PATH" \
    -t:Install \
    -f net9.0-android \
    -p:AndroidSdkDirectory="$ANDROID_SDK_PATH" \
    -p:AndroidDeviceSerial="$SELECTED_SERIAL"

if [ $? -eq 0 ]; then
    printf "${YELLOW}Restarting the app...${NC}\n"
    $ADB_PATH -s "$SELECTED_SERIAL" shell am force-stop "$PACKAGE_NAME"
    
    # Dynamically find the launcher activity
    LAUNCHER_ACTIVITY=$($ADB_PATH -s "$SELECTED_SERIAL" shell dumpsys package "$PACKAGE_NAME" | grep -A 1 "android.intent.action.MAIN:" | grep "$PACKAGE_NAME" | awk '{print $2}' | cut -d '/' -f 2 | cut -d '}' -f 1 | head -n 1)
    
    if [ -z "$LAUNCHER_ACTIVITY" ]; then
        # Fallback to the known explicit name if grep fails
        LAUNCHER_ACTIVITY="com.stefanoginobili.tripfund.app.MainActivity"
    fi

    $ADB_PATH -s "$SELECTED_SERIAL" shell am start -n "$PACKAGE_NAME/$LAUNCHER_ACTIVITY"
    printf "\n${GREEN}SUCCESS: Deployed and restarted on $SELECTED_SERIAL!${NC}\n"
else
    printf "\n${RED}[ERROR] Deployment failed.${NC}\n"
    exit 1
fi
