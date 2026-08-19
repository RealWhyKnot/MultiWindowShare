# Security Policy

## Reporting a vulnerability

Do not open a public issue for security reports. Use GitHub's private vulnerability reporting form:

https://github.com/RealWhyKnot/MultiWindowShare/security/advisories/new

I try to acknowledge new reports within 7 days and aim for an initial assessment within 14 days. There is no bug bounty.

## Scope

MultiWindowShare captures other applications' windows and audio on the local machine. Reports are in scope when they involve unintended code execution, unsafe file writes, privilege boundary issues, capture of a process the user did not select, audio or video leaving the machine through a path the user did not choose, or behavior that lets untrusted input compromise the user's machine.

Functional bugs, capture compatibility problems with particular apps, and upstream dependency issues should be filed as normal issues unless they have a concrete security impact.
