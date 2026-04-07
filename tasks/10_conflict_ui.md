**Task 7: Conflict Resolution Logic & UI**

Implement the manual conflict resolution mechanism described in `ARCHITECTURE.md`. 
1. Write the logic in the Sync Service to detect version collisions (e.g., folder `003_mario` and `003_luigi` both exist). 
2. Create an Italian UI component that alerts the user of a conflict, displays the data of both transactions side-by-side, and lets them choose the winner.
3. Saving the choice results in a new appended version folder (e.g., `004_mario`).