#!/bin/bash

# TripFund iOS Deployment Script
# Colors for output
BLUE='\033[0;34m'
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

PROJECT_PATH="src/TripFund.App/TripFund.App.csproj"
PACKAGE_NAME="com.stefanoginobili.tripfund.app.dev" # Default for Debug

printf "${BLUE}Searching for iOS Devices and Simulators...${NC}\n"

# 1. Gather Physical Devices
PHYSICAL_DEVICES=$(xcrun xctrace list devices 2>/dev/null | sed -n '/== Devices ==/,/== Simulators ==/p' | grep -v "==" | grep -v "$(hostname)" | grep -v "MacBook" | grep -v "Mac mini" | grep -v "Mac Studio")

# 2. Gather Booted Simulators
BOOTED_SIMULATORS=$(xcrun simctl list devices | grep -v "unavailable" | grep "(Booted)")

OPTIONS=()
IDS=()
TYPES=() # "Physical" or "Simulator"

# Add Physical Devices
while IFS= read -r line; do
    if [[ $line =~ (.*)\ \(([A-Z0-9-]*)\) ]]; then
        NAME="${BASH_REMATCH[1]}"
        ID="${BASH_REMATCH[2]}"
        OPTIONS+=("[Physical] $NAME")
        IDS+=("$ID")
        TYPES+=("Physical")
    fi
done <<< "$PHYSICAL_DEVICES"

# Add Booted Simulators
while IFS= read -r line; do
    if [[ $line =~ (.*)\ \(([A-Z0-9-]*)\)\ \((.*)\) ]]; then
        NAME="${BASH_REMATCH[1]}"
        ID="${BASH_REMATCH[2]}"
        STATUS="${BASH_REMATCH[3]}"
        OPTIONS+=("[Emulator] $NAME ($STATUS)")
        IDS+=("$ID")
        TYPES+=("Simulator")
    fi
done <<< "$BOOTED_SIMULATORS"

# 3. Handle Empty Lists
if [ ${#OPTIONS[@]} -eq 0 ]; then
    printf "${YELLOW}No active devices found. Listing available simulators to start...${NC}\n"
    AVAILABLE_SIMULATORS=$(xcrun simctl list devices | grep -v "unavailable" | grep -E "iPhone|iPad")
    
    if [ -z "$AVAILABLE_SIMULATORS" ]; then
        printf "${RED}[ERROR] No iOS targets found.${NC}\n"
        exit 1
    fi

    OPTIONS=()
    IDS=()
    while IFS= read -r line; do
        if [[ $line =~ (.*)\ \(([A-Z0-9-]*)\)\ \((.*)\) ]]; then
            NAME="${BASH_REMATCH[1]}"
            ID="${BASH_REMATCH[2]}"
            STATUS="${BASH_REMATCH[3]}"
            OPTIONS+=("[Emulator] $NAME ($STATUS)")
            IDS+=("$ID")
        fi
    done <<< "$AVAILABLE_SIMULATORS"

    printf "Select a Simulator to start:\n"
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

    SELECTED_ID="${IDS[$INDEX]}"
    printf "${GREEN}Booting simulator $SELECTED_ID...${NC}\n"
    xcrun simctl boot "$SELECTED_ID"
    open -a Simulator
    printf "${YELLOW}Simulator is booting. Please wait and run this script again.${NC}\n"
    exit 0
fi

# 4. Target Selection
if [ ${#OPTIONS[@]} -eq 1 ]; then
    SELECTED_ID="${IDS[0]}"
    SELECTED_TYPE="${TYPES[0]}"
    printf "Auto-selecting: ${GREEN}${OPTIONS[0]}${NC}\n"
else
    printf "Select a target iOS Device/Simulator:\n"
    for j in "${!OPTIONS[@]}"; do
        printf "  $((j+1))) ${OPTIONS[$j]}\n"
    done

    printf "Enter choice (1-${#OPTIONS[@]}): "
    read CHOICE
    INDEX=$((CHOICE-1))
    SELECTED_ID="${IDS[$INDEX]}"
    SELECTED_TYPE="${TYPES[$INDEX]}"
fi

# 5. Build and Deploy
printf "\n${BLUE}Building for $SELECTED_TYPE $SELECTED_ID...${NC}\n"

if [ "$SELECTED_TYPE" == "Simulator" ]; then
    RID="iossimulator-arm64"
    # Build only
    dotnet build "$PROJECT_PATH" -f net9.0-ios -p:RuntimeIdentifier="$RID"
    
    if [ $? -eq 0 ]; then
        printf "${YELLOW}Installing and launching...${NC}\n"
        APP_PATH="src/TripFund.App/bin/Debug/net9.0-ios/$RID/TripFund.App.app"
        xcrun simctl install "$SELECTED_ID" "$APP_PATH"
        xcrun simctl launch "$SELECTED_ID" "$PACKAGE_NAME"
        printf "${GREEN}SUCCESS: App launched!${NC}\n"
    else
        printf "${RED}[ERROR] Build failed.${NC}\n"
        exit 1
    fi
else
    RID="ios-arm64"
    # For physical devices, -t:Run is necessary but we'll try to let it finish
    dotnet build "$PROJECT_PATH" -t:Run -f net9.0-ios -p:RuntimeIdentifier="$RID" -p:_DeviceName=:v2:udid="$SELECTED_ID"
    
    if [ $? -eq 0 ]; then
        printf "\n${GREEN}SUCCESS: App launched!${NC}\n"
    else
        printf "\n${RED}[ERROR] Build or Deployment failed.${NC}\n"
        exit 1
    fi
fi
