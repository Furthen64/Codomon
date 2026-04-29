
# Hierarchy Terminology

This is the ground truth we should try our best to stick to.

```
Codebase
└── System Map
    └── System
        └── Module
            └── Code Node
```

## "Codebase" level 
The repo being analyzed.
For your example:
Codebase: Warehaus
This means everything Codomon can discover inside the repo:

	source code
	projects
	config files
	logs or log definitions
	deployment scripts
	database scripts
	docs
	generated .md summaries
	runtime trace/log imports
	manual corrections

So Codebase is the raw territory. It does not mean only C# files.


## "System Map" level
This has an active view, but several views are available. 
The System Map houses all of the Codebase's content. When I say Codebase, I mean the repo we analyse. It should house absolutely everything this product contains. 
Let's just imagine we are analysing a new product by 'Company LTD' called 'Warehaus' which is a software package 
for keeping track of a warehouse. 

The top-level Codomon model for the analyzed product.

System Map: Warehaus

The System Map houses all interpreted content from the Codebase.

It contains:

Systems
Modules
Code Nodes
External Systems
logs/config associations
startup mechanisms
runtime observations
relationships
manual overrides
LLM architecture hypotheses

The System Map can be viewed in different ways.

So the map is not one diagram. It is the structured model behind many diagrams.



## "System" level
High level, groups together multiple things, is startable/monitorable has its own log and config files. 
Can be a Desktop App, WebApp or Service running backend stuff. The name of the system should hopefully connect 
it to the logs and config files. 

Example 1 of a system in a codebase: DatabaseEngine. It starts up and does maintenance, sanity checks. 
Example 2: HRCore. This is responsible for Human Resources stuff like paychecks and who has signed in to work when. 

## "Module" level
A System is built up by Modules: Modules are libraries or building blocks to provide everything for the system. 
They have connections among themselves, they have IPC or other ways to communicate between other modules inside 
other systems. And even totally external systems to the codebase / Warehaus.

A Module is a meaningful functional building block inside one or more Systems. It may correspond to a project, 
namespace, folder, DLL, service class cluster, or LLM-inferred responsibility group.

Important: a Module can be shared.






