// Pattern: State Machine
// Finite states with guarded transitions.
// Pre-cut stub: customize the states and transition guards.

datatype State =
  | Idle
  | Active
  | Completed
  | Failed

// Transition: state x event -> state
// Guards: only valid transitions are allowed
method Transition(current: State, event: string) returns (next: State)
  ensures next == Idle || next == Active || next == Completed || next == Failed
  // approach: match on current state + event, return new state
  // invalid transitions stay in current state or go to Failed