---
name: mendix-microflow-syntax
description: Use when writing a Mendix microflow expression (any non-retrieve activity — change, decision, calculate, validate, etc.) or an XPath constraint (retrieve-from-database action) on Studio Pro 10.24.13–11.9.x. Picks the right syntax based on context. Includes full operator and function references for both, with examples and anti-patterns. Trigger any time the model needs to author Mendix syntax inside a microflow.
---

## Tools in this environment

This skill is for **Studio Pro 10.24.13–11.9.x** (concord-mcp tool surface). The Mendix studio-pro MCP server and Maia are **not available** on this version.

This skill is a syntax reference; it does not prescribe a specific tool flow. Use the `mendix-microflow-update` skill for mutation rules and the `mendix-microflow-common` skill for layout rules.

For reading or writing microflow content, use:
- `mcp__concord-mcp__read_microflow_details` — read a microflow before editing.
- `mcp__concord-mcp__update_microflow` — apply mutations to a microflow.

## When to use which syntax

**⚠️ CRITICAL DISTINCTION** — the wrong syntax in the wrong place is the most common source of "Error(s) in expression":

- **Database query** = XPath constraint with **square brackets** → use the **XPath section** below.
- **Everything else** (change object, calculate variable, decision split, validation, format string, etc.) = an Expression **without brackets** → use the **Expressions section** below.

If you are inside a `RetrieveAction`'s constraint field, you write XPath. Anywhere else in a microflow, you write an Expression.

---

# Mendix Expression Language Reference

**⚠️ CRITICAL:** Expressions are for calculations and logic inside non-retrieve activities. Do NOT use for database filtering (use XPath instead).

## Syntax Rules

- **String literals MUST use single quotes**: `'text'` (never double quotes)
- **Escape a single quote by doubling it**: `'I''m a developer'` results in `I'm a developer`
- **Variables are prefixed with `$`**: `$Variable`, `$Customer`
- **Object attributes use slash notation**: `$Object/Attribute`
- **Attribute names are case-sensitive**: Must match the domain model exactly
- **Associations use slash notation**: `$Customer/ModuleName.Association_Name/ModuleName.EntityName/Attribute`
- **Constants use `@` prefix**: `@ModuleName.ConstantName`
- **Enumerations use dot notation**: `ModuleName.EnumName.EnumValue`
- **No comments allowed** in expressions
- **All parentheses must be balanced** in function calls and conditions

⚠️ **CRITICAL**: Always verify that attributes and associations exist in the domain model before using them.

## System Variables (Microflows)

- `$latestError`: Object of type `System.Error` which contains the attributes for the latest error
- `$latestSoapFault`: Object of type `System.SoapFault` which contains the attributes for the latest SOAP fault
- `$latestHttpResponse`: Object of type `System.HttpResponse` which contains the attributes for the latest HTTP response
- `$currentSession`: Object of type `System.Session` which contains the attributes for the current session
- `$currentUser`: Object of type `System.User` which contains the attributes for the currently signed-in user
- `$currentDeviceType`: Enumeration `System.DeviceType` which contains the type of the current user's device

## Operators

**Rule**: Comparisons with `empty` are only valid for `=` and `!=`; other comparisons error if either side is `empty`.

- `+`, `-`, `*`, `div`, `:`, `mod`: Arithmetic. Numeric -> Numeric
- `<`, `>`, `<=`, `>=`, `=`, `!=`: Comparison. Returns Boolean
- `and`, `or`: Boolean logic. Boolean -> Boolean
- `not(x)`: Negation. Boolean -> Boolean
- Unary `-`: Numeric -> Numeric

Examples

- `3 div 5` -> `0.6`
- `4 > 3` -> `true`
- `not($IsValid)`

## Special Checks

**Caution**: Evaluation is left-to-right, so check `empty` before accessing attributes.

- `= empty`: Check if object or member is empty. Boolean result. `empty` can only be used as a value
- `isNew(object)`: Boolean. True for newly created, not yet committed objects

Examples

- `$Order = empty or $Order/Status = MyModule.OrderStatus.Completed`
- `isNew($Customer)`

## Conditional Expressions

- `if <condition> then <a value> else <other value>`: Conditional expression

Examples

- `if 7 > 6 then 'correct' else 'incorrect'`

## Math Functions

**Note**: Rounding mode depends on the app setting (commercial vs bankers rounding).

- `abs(n)`: Numeric -> Numeric
- `ceil(n)`: Numeric -> Integer/Long
- `floor(n)`: Numeric -> Integer/Long
- `max(n1, n2, ...)`: Numeric or Date and time -> same type
- `min(n1, n2, ...)`: Numeric or Date and time -> same type
- `pow(n, p)`: Numeric -> Decimal
- `random()`: -> Decimal between 0.0 and 1.0
- `round(n)`: Numeric -> Integer/Long
- `round(n, precision)`: Numeric, Integer/Long -> Decimal
- `sqrt(n)`: Numeric -> Decimal. Error on negative input

Examples

- `round(3.5)` -> `4`

## String Functions

**Note**: Strings use single quotes and are case-sensitive. `isMatch` is implicitly anchored. `replaceAll` and `replaceFirst` do not support backreferences.

- `toLowerCase(string)`: String -> String
- `toUpperCase(string)`: String -> String
- `substring(string, start)`: String, Integer -> String. Errors if start is past end
- `substring(string, start, length)`: String, Integer, Integer -> String. Errors if length is too long
- `find(string, substring)`: String, String -> Integer. Returns index or `-1`
- `find(string, substring, start)`: String, String, Integer -> Integer
- `findLast(string, substring)`: String, String -> Integer. Returns index or `-1`
- `findLast(string, substring, lastIndex)`: String, String, Integer -> Integer
- `contains(string, substring)`: String, String -> Boolean. Empty substring returns `true`
- `startsWith(string, prefix)`: String, String -> Boolean
- `endsWith(string, suffix)`: String, String -> Boolean
- `trim(string)`: String -> String
- `isMatch(string, regex)`: String, String -> Boolean
- `replaceAll(string, regex, replace)`: String, String, String -> String
- `replaceFirst(string, regex, replace)`: String, String, String -> String
- `urlEncode(string)`: String -> String
- `urlDecode(string)`: String -> String
- `+` (concatenation): String + (String|Number) -> String

Examples

- `substring('Hello World', 6)` -> `'World'`
- `isMatch('test123', '[a-z]+[0-9]+')` -> `true`
- `urlEncode('Hello, world!')` -> `'Hello%2C+world%21'`

## Date and Time Functions

### Date Creation

**Rule**: Accepts 1 to 6 fixed literal values only (no variables or attributes). Use `parseDateTime()` for variable input.
**Rule**: Valid ranges are year >= 1800, month 1-12, day 1-31, hour 0-23, minute 0-59, second 0-59.

- `dateTime(year, month, day, hour, minute, second)`: Date and time (local)
- `dateTimeUTC(year, month, day, hour, minute, second)`: Date and time (UTC)

Examples

- `dateTime(2024, 12, 31, 23, 59, 59)`

### Date Boundary Functions

- `beginOfDay(dateTime)`: Date and time -> Date and time
- `beginOfWeek(dateTime)`: Date and time -> Date and time
- `beginOfMonth(dateTime)`: Date and time -> Date and time
- `beginOfYear(dateTime)`: Date and time -> Date and time
- `endOfDay(dateTime)`: Date and time -> Date and time
- `endOfWeek(dateTime)`: Date and time -> Date and time
- `endOfMonth(dateTime)`: Date and time -> Date and time
- `endOfYear(dateTime)`: Date and time -> Date and time

Examples

- `beginOfDay([%CurrentDateTime%])`

### Date Difference Functions

**Behavior**: Differences are absolute. `calendarMonthsBetween` and `calendarYearsBetween` ignore time and return Integer/Long.

- `millisecondsBetween(dateTime1, dateTime2)`: -> Decimal
- `secondsBetween(dateTime1, dateTime2)`: -> Decimal
- `minutesBetween(dateTime1, dateTime2)`: -> Decimal
- `hoursBetween(dateTime1, dateTime2)`: -> Decimal
- `daysBetween(dateTime1, dateTime2)`: -> Decimal
- `weeksBetween(dateTime1, dateTime2)`: -> Decimal
- `calendarMonthsBetween(dateTime1, dateTime2)`: -> Integer/Long
- `calendarYearsBetween(dateTime1, dateTime2)`: -> Integer/Long

Examples

- `daysBetween([%CurrentDateTime%], $Task/DueDate)`

### Add Time Functions

**Note**: UTC variants exist for days, weeks, months, quarters, years (for example, `addDaysUTC`). You can pass `Long` values.

- `addMilliseconds(dateTime, milliseconds)`: Date and time, Integer -> Date and time
- `addSeconds(dateTime, seconds)`: Date and time, Integer -> Date and time
- `addMinutes(dateTime, minutes)`: Date and time, Integer -> Date and time
- `addHours(dateTime, hours)`: Date and time, Integer -> Date and time
- `addDays(dateTime, days)`: Date and time, Integer -> Date and time
- `addWeeks(dateTime, weeks)`: Date and time, Integer -> Date and time
- `addMonths(dateTime, months)`: Date and time, Integer -> Date and time
- `addQuarters(dateTime, quarters)`: Date and time, Integer -> Date and time
- `addYears(dateTime, years)`: Date and time, Integer -> Date and time

Examples

- `addDays([%CurrentDateTime%], 7)`

### Subtract Time Functions

**Note**: UTC variants exist for days, weeks, months, quarters, years (for example, `subtractDaysUTC`). You can pass `Long` values.

- `subtractMilliseconds(dateTime, milliseconds)`: Date and time, Integer -> Date and time
- `subtractSeconds(dateTime, seconds)`: Date and time, Integer -> Date and time
- `subtractMinutes(dateTime, minutes)`: Date and time, Integer -> Date and time
- `subtractHours(dateTime, hours)`: Date and time, Integer -> Date and time
- `subtractDays(dateTime, days)`: Date and time, Integer -> Date and time
- `subtractWeeks(dateTime, weeks)`: Date and time, Integer -> Date and time
- `subtractMonths(dateTime, months)`: Date and time, Integer -> Date and time
- `subtractQuarters(dateTime, quarters)`: Date and time, Integer -> Date and time
- `subtractYears(dateTime, years)`: Date and time, Integer -> Date and time

Examples

- `subtractMonths([%CurrentDateTime%], 3)`

### Trim Functions

**Note**: UTC variants exist for hours, days, months, years (for example, `trimToDaysUTC`)

- `trimToSeconds(dateTime)`: Date and time -> Date and time
- `trimToMinutes(dateTime)`: Date and time -> Date and time
- `trimToHours(dateTime)`: Date and time -> Date and time
- `trimToDays(dateTime)`: Date and time -> Date and time
- `trimToMonths(dateTime)`: Date and time -> Date and time
- `trimToYears(dateTime)`: Date and time -> Date and time

Examples

- `trimToDays([%CurrentDateTime%])`

## Type Conversion Functions

### Basic Conversions

**Note**: `toString(empty)` returns an empty string.
**Note**: If parsing fails and no default is provided, an error occurs.

- `toString(value)`: Convert non-string values to string; for enums returns the key (not the caption). Do not use when already a string
- `length(string)`: String -> Integer
- `length(list)`: List -> Integer
- `parseInteger(string)`: String -> Integer/Long
- `parseInteger(string, default)`: String, Integer/Long -> Integer/Long

Examples

- `parseInteger('not_a_number', 42)` -> `42`

### Decimal Parse and Format

**Format pattern (special pattern characters)**:

- `0`: Digit (required)
- `#`: Digit (optional)
- `.`: Decimal separator
- `,`: Grouping separator
- `-`: Minus sign
- `E`: Scientific notation exponent (use `E` + one or more `0`; no grouping with `E`)
- `;`: Separates positive/negative subpatterns
- `%`: Multiply by 100 and show percent
- `U+2030`: Multiply by 1000 and show per mille
- `U+00A4`: Currency sign (double for international currency)
- `'`: Quote specials in prefix/suffix; use `''` for a literal `'`
  **Locale**: Affects decimal/grouping separators and currency symbol. Use IETF BCP 47 (e.g., `en`, `en-US`, `fr-FR`)
  **Note**: If parsing fails and no default is provided, an error occurs

- `parseDecimal(string)`: String -> Decimal
- `parseDecimal(string, default)`: String, Decimal/empty -> Decimal
- `parseDecimal(string, format, default)`: String, String, Decimal/empty -> Decimal
- `formatDecimal(decimal, format)`: Decimal, String -> String
- `formatDecimal(decimal, format, locale)`: Decimal, String, String -> String

Examples

- `parseDecimal('3,241.98', '#,###.##')` -> `3241.98`
- `formatDecimal(0.256, '0.0%')` -> `'25.6%'`

### Date Parse and Format

**Caution**: UTC variants exist. Avoid UTC variants in client-side expressions when assigning to non-localized Date and time attributes.
**Note**: If parsing fails and no default is provided, an error occurs.

- `parseDateTime(string, format)`: String, String -> Date and time
- `parseDateTime(string, format, default)`: String, String, Date and time -> Date and time
- `parseDateTimeUTC(string, format)`: String, String -> Date and time (UTC)
- `parseDateTimeUTC(string, format, default)`: String, String, Date and time -> Date and time (UTC)
- `formatDateTime(dateTime)`: Date and time -> String
- `formatDateTime(dateTime, format)`: Date and time, String -> String
- `formatDateTimeUTC(dateTime)`: Date and time -> String
- `formatDateTimeUTC(dateTime, format)`: Date and time, String -> String
- `formatTime(dateTime)`: Date and time -> String
- `formatTimeUTC(dateTime)`: Date and time -> String
- `formatDate(dateTime)`: Date and time -> String
- `formatDateUTC(dateTime)`: Date and time -> String
- `dateTimeToEpoch(dateTime)`: Date and time -> Integer/Long
- `epochToDateTime(epoch)`: Integer/Long -> Date and time

Examples

- `formatDateTime([%CurrentDateTime%], 'yyyy-MM-dd HH:mm:ss')`
- `dateTimeToEpoch([%CurrentDateTime%])`

## Enumeration Functions

- `getCaption(enum)`: Enum -> String. Localized caption
- `getKey(enum)`: Enum -> String. Technical key

Examples

- `getCaption($Customer/Status)`

## Runtime Variables

**Note**: Date/time variables are also available in UTC format (for example, `[%CurrentDateTimeUTC%]`)

- `[%BeginOfCurrentDay%]`
- `[%BeginOfCurrentHour%]`
- `[%BeginOfCurrentMinute%]`
- `[%BeginOfCurrentWeek%]`
- `[%BeginOfCurrentMonth%]`
- `[%BeginOfCurrentYear%]`
- `[%BeginOfTomorrow%]`
- `[%BeginOfYesterday%]`
- `[%CurrentDateTime%]`
- `[%EndOfCurrentDay%]`
- `[%EndOfCurrentHour%]`
- `[%EndOfCurrentMinute%]`
- `[%EndOfCurrentWeek%]`
- `[%EndOfCurrentMonth%]`
- `[%EndOfCurrentYear%]`
- `[%EndOfTomorrow%]`
- `[%EndOfYesterday%]`
- `[%CurrentUser%]`: Current user object

## Formatting

Expressions can be split across lines with whitespace for readability. Keep it consistent within a microflow.

## Anti-Patterns (DO NOT USE)

❌ **Do not use double quotes** for strings - always use single quotes: `'text'` not `"text"`
❌ **Do not use unsupported functions** like `split()`, `join()`, `map()`, `filter()`
❌ **Do not use JavaScript/Python operators** like `===`, `!==`, `&&`, `||`, `++`, `--`
❌ **Do not use the modulo operator `%`** - use `mod` instead: `10 mod 3`
❌ **Do not chain multiple attribute accesses without checking for empty**: Check `$Order != empty` first
❌ **Do not use overly complex nested expressions** - break them into multiple microflow activities
❌ **Do not assume attributes exist** - verify the domain model structure first

## Troubleshooting

### Common Errors

Note that all of the errors below are the possible causes of a generic "Error(s) in expression" message. It is unfortunately not possible to get
more specific error message, so you must check for all of the below when troubleshooting expression errors:

- **"Cannot read attribute of empty object"**: Add null checks with `$Object != empty`
- **"Type mismatch"**: Ensure operands are compatible types
- **"Invalid function"**: Verify the function is supported and spelled correctly
- **"Attribute not found"**: Check that the attribute exists in the domain model (case-sensitive)
- **"Invalid date format"**: Use the correct format string for `formatDateTime()` or `parseDateTime()`
- **"Wrong association traversal"**: Remember that when traversing an association, you must specify the association name and the entity before specifying entity attribute:
    - CORRECT: `$RoomBooking/MyFirstModule.Booking_Room/MyFirstModule.Room/MaxOccupancy` <- follows the $Variable/Module.Association/Module.Entity/Attribute pattern
    - INCORRECT: `$RoomBooking/MyFirstModule.Room/MaxOccupancy` <- missing the association name
    - INCORRECT: `$RoomBooking/MyFirstModule.Booking_Room/MaxOccupancy` <- missing the entity name

### Performance Tips

- Avoid complex calculations in loops; pre-calculate values
- Cache frequently used expressions in variables
- Use appropriate precision; avoid Decimal when Integer/Long suffices

## Regular Expression Notes

- **Microflows**: Java regular expression syntax

## Critical Constraints

⚠️ **ONLY use functions and operators documented above** - Unsupported functions cause runtime errors
⚠️ **Use single quotes for all string literals** - Double quotes are not supported
⚠️ **Attribute and association names are case-sensitive** - Must match domain model exactly
⚠️ **Always check for empty objects** before accessing attributes to prevent errors
⚠️ **Verify domain model structure** before referencing attributes or associations
⚠️ **Keep expressions simple and readable** - Complex logic should use multiple activities

---

# XPath Constraints Reference

**⚠️ CRITICAL:** XPath is ONLY for database filtering in retrieve activities. Do NOT use for calculations or general logic (use Expressions instead).
IMPORTANT DISTINCTION: Mendix XPath differs from standard XPath, so follow the syntax below.

This skill enables you to construct XPath constraints for filtering objects in microflow retrieve actions. XPath constraints use a subset of XPath syntax and are always enclosed in square brackets `[]`.

**Note:** In Studio Pro you only write constraints (not full XPath queries), and not all XPath operators are supported there.

## Syntax Rules

- **All XPath constraints MUST be enclosed in square brackets**: `[constraint]`
- **Attribute names are referenced directly without prefixes**: `[Price > 100]`
- **String literals use single quotes**: `[Name = 'John']`
- **Association paths use forward slashes**: `[Module.Product_Order/Module.Order/Module.Order_Customer/Module.Customer]`
- **System variables use the `'[%VariableName%]'` format**: `[CreatedDate >= '[%BeginOfCurrentMonth%]']`
- **Microflow variables use the `$VariableName` format**: `[Price > $MinPrice]`
- **Parentheses are used for grouping**: `[(Name = 'Jansen' or Name = 'Smit') and Sales.Customer_Address/Sales.Address/City = 'Rotterdam']`
- **Attribute names are case-sensitive** and must match exactly

## Operators

**Note**: All operators apply inside the `[]` constraint.

- `=`: Equal to
- `!=`: Not equal to
- `<`: Less than
- `<=`: Less than or equal to
- `>`: Greater than
- `>=`: Greater than or equal to
- `or`: Logical or
- `and`: Logical and
- `+`: Addition
- `-`: Subtraction
- `*`: Multiplication
- `div`: Division

Examples

- `[Price >= 9.70 and Price < 9.80]`
- `[TotalItems * Price >= 1000]`

## Functions

### Boolean Functions

**Note**: Boolean literals can be `true()` or `false()`; `'true'`/`'false'` and `true`/`false` (due to implicit type conversion) are also accepted.

- `true()`: Boolean literal true
- `false()`: Boolean literal false
- `not(x)`: Boolean negation

### String Functions

**Note**: String comparisons are generally case-insensitive, depending on database collation.

- `length(attribute_or_string)`: String -> Integer
- `string-length(attribute_or_string)`: Alias for `length()`
- `contains(attribute, substring)`: Boolean
- `starts-with(attribute, prefix)`: Boolean
- `ends-with(attribute, suffix)`: Boolean

### Date and Time Functions

**Note**: Timezone variants accept an IANA timezone or `'UTC'`. GMT offsets are not supported. If omitted, the server timezone is used.
**Rule**: Only the functions listed here are supported in XPath constraints.

- `year-from-dateTime(attribute, timezone?)`
- `month-from-dateTime(attribute, timezone?)`
- `day-from-dateTime(attribute, timezone?)`
- `hours-from-dateTime(attribute, timezone?)`
- `minutes-from-dateTime(attribute, timezone?)`
- `seconds-from-dateTime(attribute, timezone?)`
- `quarter-from-dateTime(attribute, timezone?)`: 1-4
- `day-of-year-from-dateTime(attribute, timezone?)`: 1-366
- `week-from-dateTime(attribute, timezone?)`: 1-53; depends on database
- `weekday-from-dateTime(attribute, timezone?)`: mapping depends on database

Examples

- `[not(Name = 'Jansen')]`
- `[length(FirstName) >= 5]`
- `[year-from-dateTime(DateAttribute, 'America/New_York') = 2011]`

## Special Checks

- `= empty`: Check if object or member is empty. `empty` can only be used as a value
- Exists: Association path evaluates to true if at least one related object exists

Examples

- `[Attribute = empty]`
- `[Sales.Customer_Order/Sales.Order]`

## Implicit Type Conversions

- Number to String: Numeric in string context converts to string
- String to Number: Numeric strings in numeric context convert to number
- General conversion rule: If both sides are plain values and have different types, one side is converted to the type that occurs first in the following list:
    1. Date and time
    2. Boolean
    3. Decimal
    4. Integer/Long
    5. String

## Variables

### Microflow Variables

- `$VariableName`: Microflow parameter or variable

### System Variables

#### User and Context Variables

- `'[%CurrentUser%]'`: GUID of the current user
- `'[%CurrentObject%]'`: GUID of the current context object
- `'[%UserRole_Administrator%]'`: Administrator user role

### Date and Time Variables

- `'[%CurrentDateTime%]'`: Current date and time

**Time period variables** follow the pattern `'[%Begin/End + Period + UTC?%]'`:

- Day: `'[%BeginOfCurrentDay%]'`, `'[%EndOfCurrentDay%]'`, `'[%BeginOfYesterday%]'`, `'[%EndOfYesterday%]'`, `'[%BeginOfTomorrow%]'`, `'[%EndOfTomorrow%]'`
- Week: `'[%BeginOfCurrentWeek%]'`, `'[%EndOfCurrentWeek%]'`
- Month: `'[%BeginOfCurrentMonth%]'`, `'[%EndOfCurrentMonth%]'`
- Year: `'[%BeginOfCurrentYear%]'`, `'[%EndOfCurrentYear%]'`
- Hour/Minute: `'[%BeginOfCurrentHour%]'`, `'[%EndOfCurrentHour%]'`, `'[%BeginOfCurrentMinute%]'`, `'[%EndOfCurrentMinute%]'`

UTC variants are available by appending `UTC` (for example, `'[%BeginOfCurrentDayUTC%]'`).

### Time Length Variables

- `'[%DayLength%]'`: Length of one day
- `'[%HourLength%]'`: Length of one hour
- `'[%MinuteLength%]'`: Length of one minute
- `'[%SecondLength%]'`: Length of one second
- `'[%WeekLength%]'`: Length of one week
- `'[%MonthLength%]'`: Length of one month
- `'[%YearLength%]'`: Length of one year

Examples

- `[Price > $MinPrice]`
- `[contains(Name, $SearchTerm)]`
- `[id = '[%CurrentUser%]']`

## Common Patterns

### Filtering by Association

- `[Module.Association/Entity]`: Association exists
- `[Module.Association/Entity/Attribute = 'value']`: Filter through association
- `[not(Module.Association/Entity)]`: Association does not exist

### Combining Multiple Conditions

- `[Condition1 and Condition2 and Condition3]`
- `[Condition1 or Condition2]`
- `[(Condition1 or Condition2) and Condition3]`

### Date Range Filtering

**Rule**: When combining a time period variable with a time length variable (for example, `- 30 * [%DayLength%]`), the entire expression must be inside a single-quoted string.

- Good: `[DateAttribute >= '[%BeginOfCurrentDay%] - 30 * [%DayLength%]']`
- Bad: `[DateAttribute >= '[%BeginOfCurrentDay%]' - 30 * '[%DayLength%]']`

### Text Search Patterns

- `[contains(Name, $SearchTerm)]`
- `[contains(Name, $SearchTerm) or contains(Email, $SearchTerm)]`
- `[starts-with(Code, 'PRE')]`
- `[ends-with(Email, '@company.com')]`

### Empty and Null Checks

- `[Attribute = empty]`
- `[Attribute != empty]`
- `[Module.Association/Entity]`
- `[not(Module.Association/Entity)]`

## Example Constraints

### Simple Comparisons

- `[Age >= 18]`
- `[Status = 'Active']`

### Date Filtering

- `[CreatedDate >= '[%BeginOfCurrentMonth%]']`
- `[year-from-dateTime(BirthDate) = 1990]`

### Association-based Filtering

- `[Sales.Customer_Order/Sales.Order]`
- `[Sales.Customer_Order/Sales.Order/TotalAmount > 1000]`

### Text Search

- `[contains(Name, $SearchTerm) or contains(Email, $SearchTerm)]`

### Complex Conditions

- `[(Status = 'Active' or Status = 'Pending') and ExpiryDate > '[%CurrentDateTime%]']`

### Using System Variables

- `[CreatedById = '[%CurrentUser%]']`

## Anti-Patterns (DO NOT USE)

❌ **Do not use unsupported XPath functions** like `substring()`, `concat()`, `round()`, `sum()`, `count()`
❌ **Do not use double quotes** for strings - always use single quotes: `'text'` not `"text"`
❌ **Do not use SQL operators** like `LIKE`, `IN`, `BETWEEN`, `IS NULL`
❌ **Do not use JavaScript/Python operators** like `===`, `!==`, `&&`, `||`, `%` (use `div` for division)
❌ **Do not nest functions excessively** - keep expressions simple for performance
❌ **Do not access deeply nested associations** (more than 2-3 levels) - this impacts performance

## Troubleshooting

### Performance Tips

- Index frequently filtered attributes in your domain model
- Avoid deep association paths (more than 2-3 levels)
- Use specific conditions rather than broad `or` clauses
- Consider database retrieval limits to prevent performance issues
- Test with realistic data volumes

### Common Errors

- **"Invalid XPath"**: Check for balanced brackets and proper syntax
- **Empty results**: Verify attribute names match your entity exactly (case-sensitive)
- **Type mismatch**: Ensure comparisons use compatible types
- **Function not supported**: Verify you are using only the functions listed in this reference
- **Association path errors**: Check that association paths exist and are spelled correctly with Module.Association/Entity format

## Critical Constraints

⚠️ **ONLY use the functions and operators documented above** - Using unsupported functions will cause runtime errors
⚠️ **XPath constraints MUST always start and end with square brackets** `[ ]`
⚠️ **Keep XPath constraints as simple as possible** for better performance and maintainability
⚠️ **Attribute names are case-sensitive** - they must match exactly as defined in the domain model
⚠️ **Use single quotes for string literals**, never double quotes
⚠️ **Test your XPath constraints** with realistic data to ensure correct filtering and performance
⚠️ **System variables must always be enclosed in single quotes, square brackets and percentage characters**: `'[%SystemVariableName%]'`
