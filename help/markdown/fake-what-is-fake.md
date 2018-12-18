# F# Make - What is FAKE?

> This a general discussion about FAKE, [here you can learn about the FAKE 5 upgrade](fake-fake5-learn-more.html) or see [how to contribute](contributing.html)

[![Join the chat at https://gitter.im/fsharp/FAKE](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/fsharp/FAKE?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

"FAKE - F# Make" is a cross platform build automation system. Due to its integration 
in F#, all benefits of the .NET Framework and functional programming can be used, including 
the extensive class library, powerful debuggers and integrated development environments like 
Visual Studio or MonoDevelop, which provide syntax highlighting and code completion.

The new DSL was designed to be succinct, typed, declarative, extensible and easy to use.

See the [project home page](index.html) for tutorials and [API documentation](apidocs/v5/index.html).

## Why FAKE?

The industry movement towards "DevOps" and "infrastructure as code" leads to the fact that reproducible builds and automation becomes more and more important for basically every project.
Question is: Why do we build our automations in "old" and error-prone scripting languages like `bash`, `cmd` or `powershell` instead of a full featured language with IDE support, static typing and compile-time errors?
We believe there are different answers to that:

- The build/release process starts very simple, often with a single command. Setup a C# project or installing dependencies feels like overkill
- People use/depend on external systems and deeply integrate into those.
- Lack of awareness of other options

FAKE addresses the problem in the following ways:

- It builds on top of a fully featured statically typed and productive language (F#) with several IDE options.
- It lowers the entry point by working on top of script files. No project file or dependencies besides the fake runner.
- Remove dependencies from the CI/CD system while providing full integration. Make features available locally.
  With FAKE you can choose and switch between multiple system easily while having almost native integrations.
- Provide modules for the most commonly used tasks.
- Make it easy to start external processes through various APIs.
- Add all your automation scripts to your repository.

## When should I use FAKE

Use FAKE in the following ways:

- Remove/reduce dependencies on your CI/CD system
- Make automations reproducable and testable
- Replace existing Shell-Scripts
- Automate manual tasks

Try to not use fake:

- To replace msbuild
- To replace/rewrite existing tools you currently execute as part of your build process
  
  > Using a fake module is preferred over calling external processes directly.

Examples:

- [Scripting with FAKE](https://atlemann.github.io/fsharp/2018/06/15/standalone-scripts-with-fake-cli.html)
- [Twitter](https://twitter.com/JonathanOhlrich/status/1031591590186442753)
- [(Video) Immutable application deployments with F# Make - Nikolay Norman Andersen](https://www.youtube.com/watch?v=_sZT0CpJ6Vo)
- [(fake 4 API) Elasticsearch.Net](https://www.elastic.co/de/blog/solidifying-releases-with-fsharp-make)

## Get started

With our [quickstart](fake-dotnetcore.html) and [getting-started](fake-gettingstarted.html) guides.