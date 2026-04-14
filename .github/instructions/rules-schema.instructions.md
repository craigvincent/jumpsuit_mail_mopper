---
description: "Use when editing or creating classification rules in rules/*.json files. Validates rule structure, required fields, and consistency."
applyTo: "rules/**/*.json"
---
# Classification Rules Schema

Each rule in the `rules` array must have these fields:

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `name` | string | yes | Kebab-case identifier, unique across all rules |
| `description` | string | yes | Human-readable explanation of what this rule matches |
| `type` | string | yes | One of: `header`, `gmail-category`, `sender-domain`, `sender-pattern`, `subject-pattern` |
| `condition` | object | yes | Type-specific (see below) |
| `category` | string | yes | One of: `Newsletter`, `Marketing`, `Social`, `Notification`, `Forum`, `Automated` |
| `priority` | number | yes | Lower = higher precedence. Use 10-20 for high-confidence, 30-40 for medium, 50+ for pattern-based |

## Condition schemas by type

- **`header`**: `{ "header": "<name>", "present": true }`
- **`gmail-category`**: `{ "category": "CATEGORY_PROMOTIONS" | "CATEGORY_SOCIAL" | "CATEGORY_UPDATES" | "CATEGORY_FORUMS" }`
- **`sender-domain`**: `{ "domains": ["domain1.com", "domain2.com"] }`
- **`sender-pattern`**: `{ "patterns": ["^regex1@", "^regex2@"] }` — matched against the From address
- **`subject-pattern`**: `{ "patterns": ["regex1", "regex2"] }` — matched against the Subject line

## Conventions

- Add new domains to the existing `sender-domain` rule when possible, rather than creating a new rule
- Use case-insensitive regex patterns (the engine applies `RegexOptions.IgnoreCase`)
- Keep priority gaps (10, 20, 30...) to allow inserting rules between existing ones
- Test new rules by running: `dotnet run --project src/MailMopper -- classify --skip-ml`
