# Character reviews: a website for rating characters from TV and film

A small website where people look up characters from TV shows and films, read reviews of them, and write their own. Think of a film database, but the pages are about characters rather than titles.

## Concepts

- **Title**: a TV show or film. It has a name, a kind (`show` or `film`) and a year (the first year, for a show).
- **Character**: someone in a title. It has a name, the title it belongs to, and an optional short description (at most 280 characters).
- **Review**: one person's rating of one character. It has a reviewer name (1 to 40 characters), a rating from 1 to 5 whole stars, optional text (at most 2000 characters) and the date it was written.
- **Slug**: a URL-safe identifier made from a name. Lower case, letters and digits, with runs of anything else turned into one `-`, and no `-` at the start or end. `"Kendall Roy"` becomes `kendall-roy`. If a slug is already taken in the same scope, add `-2`, `-3`, and so on.

## Pages

All pages are server-rendered HTML. They must work with JavaScript turned off.

- C-1 `GET /`: the home page. A search box, the 10 most-reviewed characters, and the 10 newest reviews.
- C-2 `GET /search?q=<text>`: characters whose name contains the text, ignoring case and accents (`"zoe"` finds `"Zoë"`). Show at most 50, ordered by name. An empty query shows a message asking for one.
- C-3 `GET /titles/<title-slug>`: the title, with its characters, each showing average rating and review count.
- C-4 `GET /characters/<title-slug>/<character-slug>`: the character, the average rating to one decimal place, a count of reviews at each star level, and the reviews, newest first. Below them, a form to add a review.
- C-5 `POST /characters/<title-slug>/<character-slug>/reviews`: adds a review. On success, redirect (303) to the character page. On invalid input, show the form again with the user's input kept and each problem shown next to its field (status 400).
- C-6 `GET /titles/new` and `POST /titles`: a form to add a title. Duplicate name and year is an error.
- C-7 `GET /titles/<title-slug>/characters/new` and `POST /titles/<title-slug>/characters`: a form to add a character to a title. Duplicate name in the same title is an error.
- C-8 Unknown titles or characters return a 404 page with a link home.

## Rules

- C-9 The average rating is the mean of all ratings, rounded half away from zero to one decimal. A character with no reviews shows "No reviews yet" instead of an average.
- C-10 "Most reviewed" ties are broken by higher average, then by name.
- C-11 Every user-supplied value is HTML-escaped when shown.
- C-12 Leading and trailing whitespace is trimmed from all inputs before they're validated.
- C-13 The review date is the server's current date when the review is saved.
- C-14 Data must survive a restart. A single local file or embedded database is enough.

## Examples

After adding the film *Arrival* (2016), the character *Louise Banks*, and reviews rated 5, 4 and 4:

- `GET /characters/arrival/louise-banks` shows `4.3` and a breakdown of `5★ 1`, `4★ 2`, `3★ 0`, `2★ 0`, `1★ 0`.
- `POST` a review with rating `6` and an empty name: status 400, the form is shown again with "Rating must be between 1 and 5" and "Name is required", and the review text is still filled in.
- A review with the text `<script>alert(1)</script>` shows on the page as that literal text.
- Adding a second character called *Louise Banks* to *Arrival* is refused. Adding one to a different title is fine, and both have the slug `louise-banks` within their own title.
- `GET /search?q=LOUISE` finds *Louise Banks*.

## Out of scope

User accounts and logins, editing or deleting reviews, images, pagination beyond the limits above, an API.
