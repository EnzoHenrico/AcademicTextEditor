# Project Context: Cross-Platform Text Editor

## Overview
Development of a desktop text editor focused on high performance, ease of use, and native cross-platform compatibility (Windows, Linux, and macOS). The software must operate smoothly, avoiding the memory and processing bottlenecks common in web-based/Electron solutions. The project is being built using **C# and the Avalonia UI framework**.

## Core Requirements
1. **Input and Shortcut Handling:**
   - Low-level interception and processing of keyboard inputs.
   - Intelligent resolution of shortcut conflicts (global vs. focused panel scope).
   - Support for chained/multi-step shortcuts (chords).
2. **Editing and Visualization:**
   - Real-time text formatting and rendering (e.g., syntax highlighting).
   - Efficient and precise text selection management (multiple selections, exact geometry calculation for cursor/caret tracking).
   - Decoupled text processing logic to ensure the GUI updates without lag.
3. **File I/O:**
   - Native file creation, reading, and writing operations on the local disk.
   - Strict asynchronous processing to ensure large file operations do not block the main rendering thread.

## Architecture and Technical Decisions
- **Tech Stack:** C# combined with Avalonia UI.
- **UI Approach:** Leveraging Avalonia's Skia-based rendering engine to guarantee a pixel-perfect, identical visual and behavioral experience across all operating systems, bypassing local window manager quirks.
- **Event Management:** Utilizing Avalonia's robust routed events system (Tunneling/Bubbling) for fine-grained control over the data flow between hardware input and the focused UI widget.

## Developer Persona & Environment (Instructions for the AI)
- **Technical Profile:** The developer has a strong foundation in systems programming, familiarity with C++, a C# expert, and performance-oriented architecture (engine development, hardware manipulation), and also prefer a simple and readable code over unnecessary abstractions using early return and avoid high nested code.
- **Workspace Environment:** Proficient in Linux infrastructure, server administration, and structured collaborative workflows using Git (standardized commits, branch naming conventions).
- **Response Guidelines:** The Claude Code should prioritize architectural suggestions that emphasize memory efficiency, low-level execution, and clean design patterns. Avoid "black box" solutions; favor explanations that detail memory behavior, garbage collection optimization, and event routing within the C#/Avalonia ecosystem.
- **Behaviour:** The Claude Code should seggest over assume.
