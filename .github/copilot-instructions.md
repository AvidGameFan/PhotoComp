## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that **your** changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

## 5. Architectural Contraints

Refactor to the principle first, then change behavior.

1. Separation of Concerns - Keep business logic, presentation, and data access layers distinct.
2. Single Responsibility Principle - Each module or class should have one, and only one, reason to change.
3. Dependency Inversion - Depend on abstractions, not on concrete implementations.
4. Interface Segregation - Prefer many small, specific interfaces over a large, general-purpose one.
5. KISS - Keep It Simple, Stupid. Favor simplicity and clarity over cleverness and complexity.
6. YAGNI - You Aren't Gonna Need It. Avoid adding functionality until it is necessary.
7. DRY - Don't Repeat Yourself. Avoid duplicating code; abstract common functionality instead.  But don't overdo it.
8. Law of Demeter - Each unit should have limited knowledge about other units: only talk to your immediate friends.
9. Principle of Least Astonishment - Design interfaces and behaviors to match user expectations and minimize surprises.
10. Open/Closed Principle - Software entities (classes, modules, functions, etc.) should be open for extension but closed for modification.
11. Liskov Substitution Principle - Subtypes must be substitutable for their base types without altering the correctness of the program.
12. Composition Over Inheritance - Prefer composing objects with behavior over inheriting from base classes to achieve code reuse and flexibility.
13. High Cohesion & Loose Coupling - Group related functionality together (high cohesion) and minimize dependencies between modules (loose coupling).
14. Encapsulation - Keep the internal state of objects hidden and expose only what is necessary through well-defined interfaces.

Strong success criteria let you loop independently, when aligned. Weak criteria ("make it work") require constant clarification.
