**Task 2: Local Storage Service**

Based on the Models created in Task 1 and the `docs/ARCHITECTURE.md`, implement the `LocalTripStorageService`. 
1. It should handle saving and retrieving the JSON files to/from the device's local application data folder. 
2. Implement the append-only folder structure logic for transactions (`[yyyyMMddTHHmmssZ-guidprefix]/[Version]_[UserSlug]/data.json`). 
3. Ensure the read logic respects the `.deleted` file rule: if the highest version folder of a transaction contains a `.deleted` file, that transaction must be completely ignored when returning the list of active transactions.