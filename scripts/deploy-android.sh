#!/bin/bash

# TripFund Android Deployment Script
# Colors for output
BLUE='\033[0;34m'
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

PROJECT_PATH="src/TripFund.App/TripFund.App.csproj"
PACKAGE_NAME="com.stefanoginobili.tripfund.app.dev" # Default for Debug

# 0. Path Resolution
if [ ! -z "$ANDROID_HOME" ] && [ -d "$ANDROID_HOME" ]; then
    SDK_PATH="$ANDROID_HOME"
elif [ ! -z "$ANDROID_SDK_ROOT" ] && [ -d "$ANDROID_SDK_ROOT" ]; then
    SDK_PATH="$ANDROID_SDK_ROOT"
elif [ -d "$HOME/Library/Android/sdk" ]; then
    SDK_PATH="$HOME/Library/Android/sdk"
else
    printf "${RED}[ERROR] Could not find Android SDK. Please set ANDROID_HOME.${NC}\n"
    exit 1
fi

ADB_PATH="$SDK_PATH/platform-tools/adb"
EMULATOR_PATH="$SDK_PATH/emulator/emulator"
printf "${BLUE}Using Android SDK at: $SDK_PATH${NC}\n"

# 1. Check for running devices
printf "${BLUE}Searching for running Android devices...${NC}\n"
RUNNING_DEVICES=$($ADB_PATH devices | grep -v "List" | grep "device$" | awk '{print $1}')

if [ -z "$RUNNING_DEVICES" ]; then
    printf "${YELLOW}No running devices found. Searching for available Emulators (AVDs)...${NC}\n"
    AVAILABLE_AVDS=$($EMULATOR_PATH -list-avds)

    if [ -z "$AVAILABLE_AVDS" ]; then
        printf "${RED}[ERROR] No running devices or available AVDs found.${NC}\n"
        exit 1
    fi

    OPTIONS=()
    AVD_NAMES=()
    while read -r line; do
        if [ ! -z "$line" ]; then
            OPTIONS+=("$line")
            AVD_NAMES+=("$line")
        fi
    done <<< "$AVAILABLE_AVDS"

    printf "Select an Emulator to start:\n"
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

    SELECTED_AVD="${AVD_NAMES[$INDEX]}"
    printf "${GREEN}Starting emulator: $SELECTED_AVD...${NC}\n"
    $EMULATOR_PATH -avd "$SELECTED_AVD" > /dev/null 2>&1 &
    printf "${YELLOW}Emulator is booting. Please wait for it to be ready and run this script again.${NC}\n"
    exit 0
fi

# 2. Parse Running Devices into a menu
OPTIONS=()
SERIALS=()

for dev in $RUNNING_DEVICES; do
    MANUFACTURER=$($ADB_PATH -s "$dev" shell getprop ro.product.manufacturer | tr -d '\r')
    MODEL=$($ADB_PATH -s "$dev" shell getprop ro.product.model | tr -d '\r')

    if [[ "$dev" == *"emulator"* ]]; then
        AVD_NAME=$($ADB_PATH -s "$dev" emu avd name 2>/dev/null | head -n 1 | tr -d '\r')
        if [ -z "$AVD_NAME" ]; then
            AVD_NAME="$MODEL"
        fi
        LABEL="[Emulator] $AVD_NAME"
    else
        LABEL="[Physical] $MANUFACTURER $MODEL"
    fi

    OPTIONS+=("$LABEL ($dev)")
    SERIALS+=("$dev")
done

# 3. Present Menu for running devices
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
    -p:AndroidDeviceSerial="$SELECTED_SERIAL"

if [ $? -eq 0 ]; then
    printf "${YELLOW}Restarting the app...${NC}\n"
    $ADB_PATH -s "$SELECTED_SERIAL" shell am force-stop "$PACKAGE_NAME"
    
    # Dynamically find the launcher activity
    LAUNCHER_ACTIVITY=$($ADB_PATH -s "$SELECTED_SERIAL" shell dumpsys package "$PACKAGE_NAME" | grep -A 1 "android.intent.action.MAIN:" | grep "$PACKAGE_NAME" | awk '{print $2}' | cut -d '/' -f 2 | cut -d '}' -f 1 | head -n 1)

    if [ -z "$LAUNCHER_ACTIVITY" ]; then
        printf "${RED}[ERROR] Could not dynamically discover launcher activity for $PACKAGE_NAME.${NC}\n"
        exit 1
    fi

    $ADB_PATH -s "$SELECTED_SERIAL" shell am start -n "$PACKAGE_NAME/$LAUNCHER_ACTIVITY"
    printf "\n${GREEN}SUCCESS: App launched!${NC}\n"
else
    printf "\n${RED}[ERROR] Deployment failed.${NC}\n"
    exit 1
fi
