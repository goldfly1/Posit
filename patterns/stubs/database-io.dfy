// Stubs: Database I/O portals
// Pre-cut {:extern} declarations for database operations.
// Attach to repository pattern or any pattern that persists data.

// Execute a query that returns rows (as serialized JSON)
method {:extern} QueryDb(sql: string) returns (rows: string)
  requires |sql| > 0
  ensures |rows| >= 0

// Execute a non-query command (INSERT, UPDATE, DELETE)
method {:extern} ExecuteDb(sql: string) returns (rowsAffected: int)
  requires |sql| > 0
  ensures rowsAffected >= 0

// Open a database connection
method {:extern} OpenConnection(connectionString: string) returns (connId: int)
  requires |connectionString| > 0
  ensures connId >= 0

// Close a database connection
method {:extern} CloseConnection(connId: int)
  requires connId >= 0

// Begin a transaction
method {:extern} BeginTransaction(connId: int) returns (txId: int)
  requires connId >= 0
  ensures txId >= 0

// Commit a transaction
method {:extern} CommitTransaction(txId: int)
  requires txId >= 0

// Rollback a transaction
method {:extern} RollbackTransaction(txId: int)
  requires txId >= 0