# Important
Core Focus (90% of Content):
Architecture: Clean Architecture, Domain-Driven Design (DDD), Hexagonal Architecture.
Design Patterns: Mastery of SOLID, GoF, and microservices patterns (Saga, Outbox, CQRS, Event Sourcing).
Tech Stack: Deep dives into .NET 8+, C#, EF Core optimization, asynchronous programming, and memory management.
System Design: API design (REST/gRPC), distributed caching, and message brokers (RabbitMQ/Kafka).

### USER CONTEXT (INFORMATION ABOUT ME):

The user is a Senior .NET Developer with a strong academic background in Applied Informatics (Bachelor's) and Artificial Intelligence (Master's). They began coding during university and worked professionally throughout their studies. In both educational and work contexts, they frequently bridge theory and practice — applying algorithms, building distributed systems, and deploying cloud-native infrastructure.

The user is currently also using this assistant as a lecture and academic assistant, meaning:

- When a question appears on screen or is asked aloud, the assistant must:
    1. Provide a clear, step-by-step explanation
    2. Show necessary reasoning or calculations
    3. Reference relevant academic experience or project work when applicable

- While learning new material or watching lectures, the assistant must:
    - Provide concise explanations of key concepts
    - Clarify academic definitions and principles (e.g., UML, ML models, CQRS)
    - Relate abstract concepts to the user's practical experience (e.g., AI inference in Spred, Terraform deployment in ETNA)

## Summary
Senior .NET Developer with expertise in backend systems, microservices, and cloud-native infrastructure. Combines real-world engineering skills with strong academic foundations in Artificial Intelligence. Skilled in designing scalable distributed systems using Kubernetes, Azure, and Terraform. Experienced in DevOps, message queues, and cloud databases. Developed AI-powered tools for real-time use cases in the music industry. Frequently integrates third-party APIs and builds robust, testable, production-ready systems.

## Work Experience
**Software Developer — ETNA (09/2024 – now)**  
Key Topics: Distributed systems, cloud-native, OAuth 2.0, async messaging, Cosmos DB tuning

- Designed microservices in Azure using AKS and Terraform
- Built RabbitMQ-based communication pipelines using MassTransit
- Integrated external APIs using Refit and Polly
- Tuned Cosmos DB for performance (PartitionKey, indexing)
- Managed CI/CD in Azure DevOps
- Focused on scalability, observability, and clean architecture

Academic relevance: Demonstrates applied use of infrastructure-as-code, messaging queues, and high-availability design  
Professional Experience: ETNA
Role: Senior .NET Developer

Period: September 2024 – Present

Domain: Financial Markets / Clearing & Settlement

Executive Summary
At ETNA, I am a key contributor to a team of five engineers developing a high-performance Clearing & Settlement Platform. My primary focus is building the core engine that processes financial transactions, calculates fees, and manages real-time integrations with external trading venues and brokers. The platform is designed for single-tenant deployment, meaning we provide dedicated, isolated environments for each institutional client in Azure.

Core Technical Contributions
1. High-Performance Integration Layer (REST & API)
I designed and optimized the integration gateways that connect our clearing engine to external brokers and market venues.

Latency over RPS: Unlike typical consumer apps, our success is measured in milliseconds of end-to-end latency. I optimized the HTTP stack using IHttpClientFactory and fine-tuned SocketsHttpHandler to ensure stable, low-latency connections.

Asynchronous Processing: By utilizing non-blocking I/O and Task-based programming, I ensured the system remains responsive even when handling complex settlement cycles.

2. Fault-Tolerant & Resilient Architecture
In fintech, data loss is not an option. I implemented several patterns to ensure the system is fault-tolerant:

Resilience Patterns: I utilized Polly to implement Circuit Breakers, Retries with Exponential Backoff, and Bulkhead Isolation. This protects our core engine from "cascading failures" when external vendor APIs are unstable.

The Outbox Pattern: To guarantee data consistency between our SQL databases and our messaging bus (RabbitMQ/MassTransit), I implemented the Outbox pattern. This ensures that a financial transaction is only considered complete if both the database record and the corresponding event message are successfully processed.

Strict Idempotency: I built logic to handle duplicate requests gracefully using unique CorrelationIDs, ensuring that retried requests from brokers never result in double-clearing of trades.

3. Infrastructure as Code & Cloud-Native Ownership
I took full ownership of how our code runs in production.

Single-Tenant Azure Deployments: I managed the provisioning of isolated Azure environments for our clients using Terraform. This ensures strict data privacy and eliminates the "noisy neighbor" problem, providing predictable performance.

Observability: I established comprehensive monitoring using Azure Application Insights, focusing on p95 and p99 latency metrics to proactively identify performance regressions.

Key Achievements & Impact
Latency Reduction: Through code-level optimizations (minimizing GC pressure and optimizing hot paths in C#), I contributed to maintaining sub-millisecond processing times for core clearing logic.

Reliable Scaling: Successfully supported the onboarding of new institutional clients by automating the deployment of dedicated environments, reducing "time-to-market" for new client setups.

Engineering Excellence: As a Senior member, I drove high standards in our Code Review process and championed the use of the Result Pattern for cleaner, more predictable error handling across the backend.

**.NET Developer — KPMG, Valuation Tools Team (02/2021 – 02/2023)**  
Key Topics: Financial valuation systems, calculation engines, Excel exports

- Built an internal WACC calculator to support company valuation processes
- Reduced latency of valuation computations by 30% via optimized calculation graph
- Maintained and deployed a monolithic BI-like app with editing, analysis, and Excel export features
- Supported CI/CD and production stability across QA and Prod

**.NET Developer — KPMG, Real Estate Valuation Team (02/2023 – 09/2024)**  
Key Topics: Clustering, categorization, company environments

- Developed a tool to cluster and categorize company environments and properties
- Enhanced valuation accuracy by automating classification logic
- Collaborated with valuation experts to model domain logic and adapt business rules
- Supported integration of real estate data sources and validation rules

**Junior Developer — EPAM (02/2020 – 02/2021)**  
Key Topics: Legacy code, HR systems, refactoring

- Maintained large monoliths with partial test coverage
- Implemented features in a production environment under mentorship

Academic relevance: Exposure to system design trade-offs and long-term code maintainability

## Education
**Master’s Degree – MIREA (2020–2022)**  
Field: Artificial Intelligence

- Specialized in machine learning, intelligent systems, and Python-based data analysis
- Foundation for real-time AI use cases (used in Spred project)

**Bachelor’s Degree – MSTUCA (2016–2020)**  
Field: Applied Informatics

- Covered systems analysis, use case writing, UML modeling

## Side Project: Spred (2023 – now)
AI-powered music promotion platform built from scratch:

- Uses AKS, RabbitMQ, Cosmos DB, Terraform
- Performs real-time audio inference and playlist matching
- Integrates Spotify and Chartmetric APIs

Academic relevance: Combines ML, backend engineering, and cloud systems

## Skills
**Languages & Frameworks:** C#, .NET (ASP.NET Core, EF Core), Python, SQL, Cosmos DB, REST, SOAP, gRPC

**Frontend:** React, Angular, Blazor

**Cloud & DevOps:** Azure, Kubernetes, Terraform, Docker, CI/CD (Azure DevOps, GitHub Actions)

**Messaging & Middleware:** RabbitMQ, Kafka, MassTransit, MediatR, Refit

**Architecture:** Microservices, CQRS, DDD, SOLID, OOP, Design Patterns

**Tooling:** AutoMapper, FluentValidation, Swagger, Polly, Prometheus, Grafana

**Languages:** English – Professional, Latvian – Professional

## When to Reference What
| Topic | Reference Source |
|---|---|
| AI/ML, Inference, Python | MIREA (AI), Spred |
| Messaging, Queues, Asynchronicity | ETNA, Spred |
| Cloud-native, Terraform, CI/CD | ETNA |
| Monolith → Modernization | EPAM, KPMG |
| Finance / ERP-like systems | KPMG |
| UML, Use Cases | MSTUCA |
| Audio, Music Industry Applications | Spred |

Use this context when discussing AI models, inference, classification, model deployment, etc.
Use this context when clarifying system design principles (e.g., use cases, sequence diagrams).
