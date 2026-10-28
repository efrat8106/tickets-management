# הכנה לראיון – מערכת ניהול פניות

מסמך עבודה אישי: איפה כל דרישה ממומשת, למה החלטתי כך, ומה השתבש בדרך ואיך תוקן.

---

## 1. הצגה בשתי דקות

"בניתי מערכת לניהול פניות: ASP.NET Core ב-.NET 10, EF Core מול SQL Server 2022 ב-Docker, ו-Angular 18.
יש 100,000 פניות שנוצרות ב-Seed דטרמיניסטי. כל הסינון, המיון והדפדוף מתבצעים ב-DB.
עדכון סטטוס מוגן ב-Optimistic Concurrency עם `rowversion`, כך ששני משתמשים לא דורסים זה את זה,
וכל שינוי נרשם ב-Audit באותה טרנזקציה. יש Bulk של עד 100 פניות עם Partial Success,
ו-Cache ל-Summary עם Invalidation אחרי commit.
את האינדקסים בחרתי לפי מדידה: הרשימה ירדה מ-4,046 ל-282 קריאות.
יש 96 בדיקות אוטומטיות, כולל בדיקת מרוץ שמוכיחה שהאכיפה היא ב-DB."

---

## 2. איפה כל דרישה ממומשת

| דרישה באפיון | איפה בקוד | איך מוכח |
|---|---|---|
| 1. שליפה: דפדוף, סינון משולב, חיפוש, מיון, Validation, CancellationToken, DTOs | `TicketQueryService.GetTicketsAsync`, `TicketQueryValidator`, `TicketsController.GetTickets`, `TicketDto`, `PagedResult` | `TicketQueryTests`, `TicketQueryValidatorTests`, SQL ב-log |
| 1. Aggregations (3) | `TicketSummaryService` – לפי סטטוס, לפי עדיפות, Top 10 מטפלים | בדיקת Summary + SQL `GROUP BY` |
| 2. עדכון סטטוס, 404, מעבר אסור, Concurrency | `TicketCommandService.UpdateStatusAsync` / `ApplyStatusChangeAsync`, `StatusTransitions`, `TicketConfiguration` (`IsRowVersion`) | `TicketStatusUpdateTests` (כולל בדיקת המרוץ), `StatusTransitionsTests` |
| 3. Audit + endpoint היסטוריה | `Ticket.ChangeStatus` מחזיר `TicketStatusHistory`; `GetHistoryAsync`; `GET /api/tickets/{id}/history` | Audit נבדק בתוכן ובכמות |
| 4. Bulk עד 100 | `BulkUpdateStatusAsync`, `BulkUpdateStatusRequestValidator`, `POST /api/tickets/bulk-status` | `TicketBulkUpdateTests` |
| 5. Angular | `tickets.store.ts` (לוגיקה), `ticket-list` (חכם), `ticket-filters`, `ticket-summary`, `status-select`, `bulk-update-bar`, `ticket-history` (תצוגה), `tickets-api.service.ts` (HTTP), interceptors | `tickets.store.spec.ts`, `tickets-api.service.spec.ts`, בדיקה בדפדפן אמיתי |
| 6. ביצועים | `docs/PERFORMANCE.md`, `docs/sql/` (שאילתות, `measure.ps1`, תוצאות גולמיות, plans), Migration `AddIndexes` | לפני/אחרי ב-`docs/sql/results` |
| 7. Cache | `ISummaryCache`, `MemorySummaryCache`, Invalidation ב-`TicketCommandService`, `docs/CACHE.md` | `TicketSummaryCacheTests`, `MemorySummaryCacheTests` |
| 8. בדיקות | `tests/Tickets.Tests`, `client/**/*.spec.ts`, `docs/TESTING.md` | 96 ירוקות |
| 9. תכנון עבודה | `docs/PLAN.md` (34 משימות, תלויות, הערכות, Traceability) | – |
| 10. איכות קוד | `GlobalExceptionHandler`, `CorrelationIdMiddleware`, `FluentValidationFilter`, `ValidationProblemResults`, `DependencyInjection.cs` (Lifetimes) | Build עם warnings-as-errors |
| 11. תיעוד | `README.md` + `docs/` | הרצה נקייה מאפס לפי ה-README |
| נתונים: 100k, יצירה חוזרת | `SeedOptions`, `SeedDataGenerator`, `TicketSeeder` | אותו checksum בכל `--reset` |

---

## 3. החלטות ונימוקים

| החלטה | למה | חלופה שנשקלה |
|---|---|---|
| .NET 10 במקום .NET 8 | LTS נוכחי; .NET 8 לא היה מותקן ותמיכתו מסתיימת ב-11/2026 | .NET 8 |
| SQL Server + EF Core | נתונים רלציוניים, `rowversion` מובנה, אינדקסים, `GROUP BY`; EF מתרגם `IQueryable` ל-SQL שאימתתי | MongoDB, Dapper |
| `int IDENTITY` | צר (המפתח מועתק לכל אינדקס), עולה תמיד – אין page splits | GUID (רחב פי 4, מפוזר) |
| Enums כ-`tinyint` עם ערכים מפורשים + CHECK | בית אחד; **מיון נכון** (כמחרוזת Priority היה High, Low, Medium); שינוי סדר ב-enum לא משנה נתונים | `varchar`, טבלת lookup |
| `nvarchar` חסום (200/200/100) | עברית; `max` לא יכול להיות מפתח באינדקס | `nvarchar(max)` |
| Optimistic Concurrency עם `rowversion` | אין נעילות בזמן שהמשתמש חושב; ה-DB אוכף אטומית | נעילה פסימית, Last write wins |
| RowVersion ב-body ולא `If-Match` | ב-Bulk צריך גרסה לכל פריט; חוזה אחיד לשני ה-endpoints | ETag + 412 |
| בדיקת RowVersion מוקדמת לפני בדיקת המעבר | לקוח שראה סטטוס ישן צריך "רענן" (409), לא הודעת מעבר מטעה (422) | – |
| 422 לכלל עסקי, 400 לקלט שבור | הבחנה ברורה בין "הקלט לא תקין" ל"הפעולה אסורה עכשיו" | 400 לכול |
| Audit באותו `SaveChanges` | אם העדכון נכשל אין Audit – עקביות | כתיבה נפרדת |
| Bulk: Partial Success, טרנזקציה לכל פריט | התנגשות בפריט אחד לא מבטלת 99; התוצאה לכל פריט מאפשרת retry | All-or-Nothing |
| Bulk: אותה פונקציה בדיוק כמו עדכון בודד | אין שכפול לוגיקה; כל הכללים זהים | – |
| Cache רק ל-Summary | יקר, נקרא הרבה, זהה לכולם, ישנות קצרה מקובלת | Cache לרשימה (מפתחות רבים, חייב להיות טרי) |
| Invalidation רק אחרי commit, ב-Bulk ב-`finally` | כישלון לא שינה כלום; ביטול באמצע עדיין השאיר פריטים שמורים | – |
| מונה גרסה ב-Cache | חישוב שהתחיל לפני עדכון לא ייכתב בחזרה אחרי ה-Invalidation | – |
| חיפוש prefix | `LIKE 'term%'` = Index Seek; `%term%` = סריקה מלאה | Contains / Full-Text |
| Controllers + Services, בלי Repository/MediatR | פשוט וברור; EF הוא כבר Unit of Work | CQRS, MediatR, Repository |
| FluentValidation דרך filter אחד | פורמט שגיאה אחד לכל המקורות, מפתחות camelCase | DataAnnotations |
| Angular: Store לכל מסך עם Signals + RxJS | מצב מקומי למסך, בלי Boilerplate | NgRx |
| השוואת חיפוש למצב הנוכחי במקום `distinctUntilChanged` | `distinctUntilChanged` היה בולע אותו חיפוש אחרי "ניקוי סינון" | `distinctUntilChanged` |
| בדיקות על Testcontainers | `rowversion` אמיתי; InMemory/SQLite לא מוכיחים Concurrency | InMemory, SQLite |
| FluentAssertions 7.2 | גרסה 8 ברישיון מסחרי | v8 |
| אינדקסים לפי מדידה | כל אינדקס עולה בכתיבה; השערות שלא עזרו נדחו | "ליתר ביטחון" |

---

## 4. זרימות שכדאי לדעת לספר

**עדכון סטטוס:** טעינה ← 404 אם אין ← 409 אם ה-RowVersion של הלקוח ישן ← 422 אם המעבר אסור ← קביעת RowVersion
של הלקוח כ-OriginalValue ← `ChangeStatus` (Status + UpdatedAt + Audit) ← `SaveChanges` אחד ←
`UPDATE ... WHERE Id=@id AND RowVersion=@orig` ← 0 שורות = `DbUpdateConcurrencyException` = 409 עם `currentTicket` ←
הצלחה = 200 עם RowVersion חדש + Invalidation ל-Cache.

**Bulk:** Validation לכל הבקשה ← שאילתה אחת `WHERE Id IN (...)` ← כל פריט דרך `ApplyStatusChangeAsync` בטרנזקציה
משלו ← בכישלון Concurrency: `Detach` כדי שה-`SaveChanges` הבא לא ישלח שוב את השינוי הכושל ← תוצאה לכל פריט לפי הסדר ←
Invalidation פעם אחת בסוף.

**חיפוש ב-Angular:** הקלדה ← `debounceTime(300)` ← השוואה ל-query הנוכחי ← עדכון query (עמוד 1) ← `switchMap`
מבטל את בקשת ה-HTTP הקודמת ← תשובה ישנה לא דורסת חדשה. בבדיקה בדפדפן: הקלדת "Invoice" = בקשה אחת.

**409 ב-Angular:** ה-interceptor הופך את השגיאה ל-`ApiError` ← ה-Store מחליף את השורה ב-`currentTicket` ← הודעה
"עודכנה על ידי משתמש אחר" ← המשתמש מחליט מחדש; ניסיון חוזר נשלח עם ה-RowVersion החדש.

---

## 5. ביצועים – המספרים

| תרחיש | קריאות לפני ← אחרי | זמן חם לפני ← אחרי |
|---|---|---|
| רשימה ברירת מחדל | 4,046 ← 282 | 53 ← 12 ms |
| סטטוס + עדיפות | 3,946 ← 694 | 44 ← 6 ms |
| מטפל + טווח תאריכים | 3,946 ← 1,964 | 48 ← 3 ms |
| חיפוש prefix | 3,946 ← 862 | 71 ← 7 ms |
| Summary | 5,919 ← 988 | 118 ← 63 ms |
| עמוד עמוק (OFFSET 79,980) | 2,073 ← 2,073 | ~316 ← ~308 ms |

- **השערות:** H1, H2, H4, H5 נשארו. H3 שונתה ל-`(AssignedTo, CreatedAt) INCLUDE (Status)` (Seek אחד במקום צירוף
  שני אינדקסים). H6 ואינדקס על `UpdatedAt` נדחו.
- **Keyset Pagination שנמדד:** 65–72 קריאות, 0–3 ms, אותן שורות.
- **Logical reads הם המדד היציב;** זמנים בסדר גודל של מילישניות קופצים בין הרצות.

---

## 6. בדיקות – מה מוכיח מה

- **בדיקת המרוץ** (`Write_between_our_read_and_our_save_is_rejected_by_the_database`): interceptor כותב מחיבור אחר
  בין הקריאה לשמירה. רק ה-DB יכול לתפוס את זה – **זו ההוכחה לאכיפה ב-DB**.
- **הבדיקה המקבילית** (10 בקשות): מאמתת התנהגות בלבד. בבדיקת מוטציה (`rowversion` לא concurrency token) היא עדיין
  עברה.
- **בדיקות מוטציה:** הסרת שובר שוויון, concurrency token, `Detach`, Invalidation, בדיקת גרסה ב-Cache, `switchMap`
  ו-debounce – בכל פעם נכשלה בדיוק הבדיקה המתאימה.

---

## 7. כל הבעיות מהיומן

סוג: **Bug** – תקלה שתוקנה · **Environment** – סביבה · **Expected** – נראה כמו תקלה אבל תקין בשלב הזה.

| # | סוג | מה היה | למה | איך תוקן / הלקח |
|---|---|---|---|---|
| 1 | Environment | .NET 8 לא מותקן | רק 6, 7, 10 על המחשב | עבר ל-.NET 10 (LTS) |
| 2 | Environment | Docker לא רץ | – | הופעל Docker Desktop; תנאי מקדים ב-README |
| 3 | Bug | ה-build נכשל על חבילה פגיעה (`System.Security.Cryptography.Pkcs` 9.0.11) | הגיעה בעקיפין דרך SqlClient; warnings-as-errors | הוצמדה גרסה מתוקנת 10.0.14, לא השתקה |
| 4 | Bug | `docker compose` נכשל: "project name must not be empty" | שם התיקייה בעברית | `name: tickets` בקובץ |
| 5 | Bug | `AddValidatorsFromAssembly` לא נמצא | החבילה בפרויקט הלא נכון | הועברה ל-Application |
| 6 | Expected | `/health` החזיר 503 | ה-DB עוד לא נוצר | נבדק אחרי Migration: 200 |
| 7 | Expected | אזהרת EF על חוסר קונפיגורציות | עוד לא היו יישויות | נעלמה ב-T-07 |
| 8 | Bug | ה-API עלה אבל הטבלאות לא נוצרו | הרצה עם `--no-build` אחרי הוספת Migration | Build מחדש; **לקח:** לבדוק את הסכמה עצמה, לא לסמוך על health |
| 9 | Bug | בדיקת יחידה ציפתה ל-`status[0]` וקיבלה `Status[0]` | המרת camelCase מוגדרת ב-DI, שבדיקת יחידה לא מריצה | תוקנה הבדיקה, לא ה-Validator |
| 10 | Bug | מפתחות שגיאה ב-PascalCase | שגיאות binding לא עוברות ב-FluentValidation | נרמול במקום אחד (`ValidationProblemResults`) |
| 11 | Bug | הודעת Validation בעברית | FluentValidation לוקח שפה מה-OS של השרת | ביטול לוקליזציה – הכול באנגלית |
| 12 | Bug | Content-Type של 400 לא היה `problem+json` | `[Produces]` על ה-controller דרס | הוסר |
| 13 | Bug | טענה שגויה שלי: "`status=7` עובר binding" | הנחה לא נכונה | נבדק ב-HTTP, תוקן התיעוד; הכלל נשאר כהגנה |
| 14 | Bug | Summary החזיר 500 | EF לא מתרגם מיון על record שנבנה בבנאי | מיון על טיפוס אנונימי, DTO בסוף |
| 15 | Bug | `UpdatedAt` בתשובה שונה מ-GET אחר כך | 7 ספרות בזיכרון מול `datetime2(3)` ב-DB | קיטוע למילישניות לפני השמירה |
| 16 | Bug | 400 חשף שם טיפוס פנימי + שגיאה כפולה | הודעות serializer ו-Required מרומז | `AllowInputFormatterExceptionMessages=false`, ביטול Required מרומז |
| 17 | Bug | מפתח header הפך ל-`x-User-Name`; גוף ריק עם מפתח `""` | נרמול עיוור | מפתחות עם `-` נשמרים; ריק = `body` |
| 18 | Bug | `{}` החזיר **500** | FluentValidation המשיך לכלל האורך על null | `Cascade(CascadeMode.Stop)` + בדיקת רגרסיה |
| 19 | Bug | בדיקת מוטציה "עברה" בטעות | המוטציה גרמה לאזהרה, ה-build נכשל, הבדיקות רצו על בינארים ישנים | חזרה עם מוטציה שמתקמפלת; הסקריפט מדפיס תוצאת build |
| 20 | Bug | דוגמת Bulk ב-`requests.http` לא תאמה את התיאור; רווח לא מקודד ב-URL | מעבר New→Waiting אסור | הרצתי את כל הקובץ ותיקנתי |
| 21 | Bug | PLAN.md הושחת (47 שורות) | PowerShell 5.1 קרא UTF-8 כ-ANSI וכתב עם BOM | שוחזר מה-commit הקודם; **לקח:** לא לערוך מסמכים ב-PowerShell |
| 22 | Bug | אחרי Bulk חלקי, פריטים שנכשלו נשארו עם RowVersion **ישן** | העתקת ה-selection לפני הרענון ודריסה בסוף | נתפס בבדיקת יחידה; רענון קודם, הסרה אחר כך |
| 23 | Bug | המדידה הראשונה הראתה 0 ms | ב-PowerShell כל `-match` דורס את `$Matches` | סדר תנאים תוקן, המדידה הורצה מחדש |
| 24 | Bug | `npm ci` נכשל ב-clone נקי | קובץ ה-lock לא תאם; אצלי עבד כי `node_modules` היה קיים | lock נוצר מחדש; **לקח:** רק הרצה מאפס תופסת את זה |
| 25 | Environment | `npm audit`: 53 חולשות, 8 בחבילות runtime | Angular 18 כבר לא נתמך | תועד; המלצה: `ng update` לגרסה נתמכת |
| 26 | Bug | סקריפט הדפדפן דיווח כישלונות שלא היו באפליקציה | טאב ברקע לא מריץ `requestAnimationFrame`; סטטוס יעד קבוע; ציפייה ל-2 רשומות היסטוריה | תיקוני סקריפט בלבד; 17/17 |

**המסר לראיון:** רוב הבאגים נמצאו כי כל תוצאה נבדקה בהרצה אמיתית ולא רק בקריאת קוד, וכי תוצאות "טובות מדי"
(בדיקה שעוברת מיד, 0 ms, health ירוק) נחקרו ולא התקבלו כמובנות מאליהן.

---

## 8. שאלות צפויות

- **למה לא נעילה פסימית?** מחזיקה משאבים בזמן שהמשתמש חושב; התנגשויות נדירות.
- **למה גם בדיקה מוקדמת וגם ב-DB?** המוקדמת זולה ומדויקת בהודעה; רק ה-DB מגן ממרוץ.
- **מה קורה עם 409 ב-UI?** השורה מתרעננת מ-`currentTicket`, הודעה, המשתמש מחליט שוב.
- **למה 200 ב-Bulk ולא 207?** 207 שייך ל-WebDAV; 200 עם תוצאה לכל פריט ברור יותר.
- **איך תעשי All-or-Nothing?** טרנזקציה אחת סביב הלולאה, rollback בכישלון הראשון.
- **למה רק ה-Summary ב-Cache?** ראה סעיף 3.
- **מה עם כמה שרתים?** Redis מאחורי `ISummaryCache`, או Pub/Sub ל-Invalidation; עד אז 60 שניות הן הגבול.
- **למה האינדקס על `UpdatedAt` נדחה?** מיון לפיו עולה ~60 ms בלי אינדקס, והעמודה משתנה בכל עדכון.
- **איך נמנעת טעינה לזיכרון?** `IQueryable` עד הסוף, Projection ל-DTO, `COUNT` נפרד, `GROUP BY` ב-SQL.
- **מה היית משפרת?** Keyset Pagination, שדרוג Angular, autocomplete לארגון ומטפל, ספירה מקורבת.
- **איך השתמשת ב-AI?** ראה README סעיף 11 ו-`docs/AI_USAGE.md`.

---

## 9. מגבלות ידועות

- OFFSET איטי בעמודים עמוקים (נמדד; Keyset הוא הפתרון).
- Cache בזיכרון לכל שרת בנפרד.
- Angular 18 לא נתמך (`npm audit`).
- אין אימות משתמשים; `ChangedBy` מ-header.
- חיפוש prefix בלבד.
- טווח תאריכים ב-UI לפי ימי UTC.
