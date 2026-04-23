# Codex.md — Developer Instructions for RoadTraffic Tool MSFS2024

## Role
You are the implementation developer. Your job is to execute small, controlled changes inside the existing project without breaking the working baseline.

You are **not** the product owner and **not** the architect.

## Baseline you must protect
Current working baseline:
- traffic is injected onto roads below / around the player aircraft
- roads come from OSM / Overpass
- spawning is done through SimConnect
- placeholder spawned objects are currently **rhinos**
- this rhino/object spawning baseline is the regression anchor

Important:
- Rhinos are only a placeholder for spawn / visibility / pipeline testing.
- Later they may be replaced with simple low-poly car models.
- Until that replacement is explicitly requested and validated, preserve the rhino baseline.

## Absolute rules
1. Do not build a parallel engine beside the existing main/baseline implementation.
2. Do not silently move work away from the original source-of-truth files.
3. Do not introduce new feature systems unless explicitly requested.
4. Do not mix refactor and experimental feature work in one change set.
5. Do not do big-bang rewrites.
6. Do not remove working behavior unless explicitly instructed.
7. Every change must keep the solution in a buildable, reviewable state.
8. All existing WPF controls in the window must continue to work in realtime.

## Preferred implementation style
- small slices
- explicit diffs
- minimal blast radius
- maintainable code
- clear responsibilities
- dependency injection where appropriate
- no hidden redesign

## What to preserve
Preserve:
- current main/baseline structure as source of truth
- working spawn pipeline
- realtime WPF interactions
- current road loading flow unless task explicitly changes it
- existing build path / runtime behavior unless task explicitly changes it

## What to avoid
Avoid:
- creating a second architecture in parallel folders/projects
- changing multiple subsystems in one step
- mixing refactor, logging, spawning experiments, visual tier systems, and new traffic representations together
- architecture changes that are not tied to a concrete problem
- unrequested optimizations
- speculative abstractions

## How to execute tasks
For each task:
1. Restate the exact scope internally.
2. Touch only the files needed.
3. Keep the working baseline behavior unless the task explicitly changes it.
4. Prefer the smallest possible change that solves the requested problem.
5. Build after the change.
6. Report exactly:
   - which files changed
   - what was changed
   - what was intentionally not changed
   - whether build passed
   - whether warnings exist
   - what regression risk remains

## Required reporting format
After each task, report:
- Changed files
- Summary of implementation
- Build result
- Warning count
- Known risks / limitations

## Specific guidance for this project
- If architecture cleanup is requested, work inside the current baseline, not beside it.
- If a refactor is requested, preserve behavior first.
- If a feature is requested, do not redesign unrelated areas.
- If a change risks the current rhino spawn pipeline, stop and call it out.
- If there is ambiguity between old baseline and new experimental structure, prefer the original main baseline and explicitly mention the conflict.

## Future evolution awareness
You should understand the intended direction:
- current placeholder = rhinos
- next practical replacement = simple low-poly cars
- possible later direction = cheaper distant ambience representation for low-light conditions

But:
- do not implement future-direction ideas unless explicitly instructed
- do not optimize the codebase for a speculative future at the cost of current baseline stability

## Default tone
Be precise, disciplined, transparent, and conservative.
Prefer:
- safe progress
- small diffs
- preserved behavior
over:
- clever redesign
- broad rewrites
- premature system building
