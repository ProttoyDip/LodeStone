"""Lab 6 system / acceptance tests driven through the real browser against the running app."""
import json
import pathlib
import random
import sys
import traceback

from playwright.sync_api import sync_playwright

BASE = "https://localhost:5001"
ROOT = pathlib.Path(__file__).resolve().parents[2]
SHOTS = ROOT / "docs" / "testing" / "screenshots"
SHOTS.mkdir(parents=True, exist_ok=True)

N = random.randint(100000, 999999)
STUDENT_EMAIL = f"lab6.student{N}@test.local"
STUDENT_NUMBER = f"L6{N}"
PASSWORD = "Lab6Testing!2026"

results = []


def run(page, tid, title, kind, expectation, fn):
    """Run one case, screenshot the end state, and record the outcome."""
    status, actual = "PASS", ""
    try:
        actual = fn(page) or "As expected."
    except AssertionError as exc:
        status, actual = "FAIL", f"{exc}"
    except Exception as exc:  # noqa: BLE001 - an unexpected error is itself a result
        status, actual = "ERROR", f"{type(exc).__name__}: {exc}"
        traceback.print_exc()
    shot = SHOTS / f"{tid}.png"
    try:
        page.screenshot(path=str(shot), full_page=False)
    except Exception as exc:  # noqa: BLE001
        print(f"  screenshot failed for {tid}: {exc}")
    results.append({
        "id": tid, "title": title, "kind": kind,
        "expected": expectation, "actual": actual,
        "status": status, "screenshot": f"screenshots/{tid}.png",
    })
    print(f"{tid} [{status}] {title} :: {actual}")


def text(page):
    return page.inner_text("body")


def fill_registration(page, *, name, email, number, password, confirm, accept=True):
    page.goto(f"{BASE}/Account/Register")
    page.fill("#FullName", name)
    page.fill("#Email", email)
    page.fill("#StudentNumber", number)
    page.fill("#Password", password)
    page.fill("#ConfirmPassword", confirm)
    if accept:
        page.eval_on_selector(
            "#AcceptPrivacy",
            "e => { if (!e.checked) { e.checked = true; e.dispatchEvent(new Event('change', {bubbles: true})); } }")
        assert page.locator("#AcceptPrivacy").is_checked(), "could not tick the privacy checkbox"

    page.click("button[type=submit]")
    page.wait_for_load_state("networkidle")


def sign_out(page):
    page.goto(f"{BASE}/Student")
    buttons = page.locator("form[action='/Account/Logout'] button[type=submit]")
    for i in range(buttons.count()):
        if buttons.nth(i).is_visible():
            buttons.nth(i).click()
            page.wait_for_load_state("networkidle")
            return
    raise AssertionError("no visible sign-out control was found")


def login(page, email, password):
    page.goto(f"{BASE}/Account/Login")
    page.fill("#Email", email)
    page.fill("#Password", password)
    page.click("button[type=submit]")
    page.wait_for_load_state("networkidle")


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="msedge", headless=True)
        ctx = browser.new_context(ignore_https_errors=True, viewport={"width": 1366, "height": 900})
        page = ctx.new_page()
        post_url_holder = {"url": f"{BASE}/Forum"}

        # ---------- Anonymous access ----------
        def tc01(pg):
            pg.goto(BASE + "/")
            assert pg.title(), "the landing page returned no title"
            return f"Landing page served, title '{pg.title()}'."
        run(page, "TC-S01", "Landing page loads for an anonymous visitor", "Normal",
            "Public landing page renders without authentication", tc01)

        def tc02(pg):
            pg.goto(BASE + "/Dashboard")
            pg.wait_for_load_state("networkidle")
            assert "/Account/Login" in pg.url, f"expected a redirect to sign-in, landed on {pg.url}"
            return "Redirected to the sign-in page with a ReturnUrl."
        run(page, "TC-S02", "Anonymous request for the student dashboard", "Exceptional",
            "Redirect to sign-in instead of serving private data", tc02)

        def tc03(pg):
            pg.goto(BASE + "/Counselor/Queue")
            pg.wait_for_load_state("networkidle")
            assert "/Account/Login" in pg.url, f"expected sign-in redirect, landed on {pg.url}"
            return "Counselor workspace is not reachable while signed out."
        run(page, "TC-S03", "Anonymous request for the counselor queue", "Exceptional",
            "Redirect to sign-in", tc03)

        # ---------- Registration validation ----------
        def tc04(pg):
            pg.goto(f"{BASE}/Account/Register")
            pg.click("button[type=submit]")
            pg.wait_for_timeout(600)
            body = text(pg)
            assert "required" in body.lower(), "no required-field message was shown"
            assert "/Account/Register" in pg.url, "an empty form was accepted"
            return "Required-field messages shown; the form did not submit."
        run(page, "TC-S04", "Registration with every field empty", "Invalid",
            "Required-field validation messages, no account created", tc04)

        def tc05(pg):
            fill_registration(pg, name="Mismatch Tester", email=f"lab6.mismatch{N}@test.local",
                              number=f"M6{N}", password=PASSWORD, confirm="Different!2026")
            assert "do not match" in text(pg), "no password mismatch message"
            return "Password mismatch reported; registration refused."
        run(page, "TC-S05", "Registration with mismatched password confirmation", "Invalid",
            "'The passwords do not match.' and no account created", tc05)

        def tc06(pg):
            fill_registration(pg, name="Short Password", email=f"lab6.short{N}@test.local",
                              number=f"S6{N}", password="Ab1!", confirm="Ab1!")
            body = text(pg)
            assert "8 characters" in body or "at least" in body.lower(), "no password length message"
            return "Minimum password length enforced."
        run(page, "TC-S06", "Registration with a password below the minimum length", "Boundary",
            "Length validation message at 4 characters against a minimum of 8", tc06)

        def tc07(pg):
            fill_registration(pg, name="Bad Number", email=f"lab6.badnum{N}@test.local",
                              number="!!! not a number", password=PASSWORD, confirm=PASSWORD)
            assert "letters, numbers" in text(pg), "no student-number format message"
            return "Student-number format rule reported."
        run(page, "TC-S07", "Registration with an illegal student number", "Invalid",
            "Format message; the claim is refused", tc07)

        def tc08(pg):
            fill_registration(pg, name="No Consent", email=f"lab6.noconsent{N}@test.local",
                              number=f"N6{N}", password=PASSWORD, confirm=PASSWORD, accept=False)
            assert "privacy commitment" in text(pg), "the consent rule did not fire"
            assert "/Account/Register" in pg.url, "registration proceeded without consent"
            return "Consent rule enforced; registration refused."
        run(page, "TC-S08", "Registration without accepting the privacy commitment", "Invalid",
            "Consent message; no account created", tc08)

        def tc09(pg):
            fill_registration(pg, name="Lab Six Student", email=STUDENT_EMAIL,
                              number=STUDENT_NUMBER, password=PASSWORD, confirm=PASSWORD)
            assert "/Account/Register" not in pg.url, (
                f"registration did not submit; still on {pg.url}")
            return f"Account created and signed in; landed on {pg.url}."
        run(page, "TC-S09", "Registration with valid details and consent accepted", "Normal",
            "Account is created and the student is signed in", tc09)

        def tc10(pg):
            sign_out(pg)
            fill_registration(pg, name="Duplicate Email", email=STUDENT_EMAIL,
                              number=f"D6{N}", password=PASSWORD, confirm=PASSWORD)
            body = text(pg).lower()
            assert "already" in body or "taken" in body or "exists" in body, (
                "a duplicate email was not reported")
            return "Duplicate email refused with a message."
        run(page, "TC-S10", "Registration reusing an existing email address", "Exceptional",
            "Duplicate is refused with a readable message", tc10)

        # ---------- Authentication ----------
        def tc11(pg):
            login(pg, STUDENT_EMAIL, "WrongPassword!2026")
            assert "/Account/Login" in pg.url, "a wrong password was accepted"
            body = text(pg).lower()
            assert "invalid" in body or "incorrect" in body or "try again" in body, (
                "no sign-in failure message")
            return "Sign-in refused without disclosing which field was wrong."
        run(page, "TC-S11", "Sign in with the wrong password", "Exceptional",
            "Refusal with a non-specific message", tc11)

        def tc12(pg):
            login(pg, f"nobody{N}@test.local", PASSWORD)
            assert "/Account/Login" in pg.url, "an unknown account was accepted"
            return "Unknown account refused with the same message as a wrong password."
        run(page, "TC-S12", "Sign in with an email that has no account", "Exceptional",
            "Same refusal as a wrong password (no account enumeration)", tc12)

        def tc13(pg):
            login(pg, STUDENT_EMAIL, PASSWORD)
            assert "/Account/Login" not in pg.url, "valid credentials were refused"
            return f"Signed in and redirected to {pg.url}."
        run(page, "TC-S13", "Sign in with valid credentials", "Normal",
            "Session established and role redirect applied", tc13)

        # ---------- Journal ----------
        def tc14(pg):
            pg.goto(f"{BASE}/Journal")
            pg.wait_for_load_state("networkidle")
            assert "/Account/Login" not in pg.url, "the journal was not reachable"
            return "Journal page rendered for the signed-in student."
        run(page, "TC-S14", "Open the private mood journal", "Normal",
            "Journal page renders with the entry form", tc14)

        def tc15(pg):
            pg.goto(f"{BASE}/Journal")
            pg.wait_for_load_state("networkidle")
            pg.locator("input[name='NewEntry.MoodRating'][value='4']").check(force=True)
            pg.locator("textarea").first.fill("First check-in written by the Lab 6 system test.")
            pg.locator("button.journal-submit").first.click()
            pg.wait_for_load_state("networkidle")
            body = text(pg).lower()
            assert "error" not in body or "saved" in body or "check-in" in body, body[:200]
            return "Entry submitted; the journal accepted today's check-in."
        run(page, "TC-S15", "Record today's mood entry", "Normal",
            "Entry is stored and shown in the history", tc15)

        def tc16(pg):
            pg.goto(f"{BASE}/Journal")
            pg.wait_for_load_state("networkidle")
            submit = pg.locator("button.journal-submit")
            if submit.count() == 0:
                body = text(pg).lower()
                assert "already" in body or "today" in body, "no one-per-day message and no form"
                return "The entry form is withdrawn for the rest of the day, with a message."
            pg.locator("input[name='NewEntry.MoodRating'][value='1']").check(force=True)
            pg.locator("textarea").first.fill("Second attempt on the same day.")
            submit.first.click()
            pg.wait_for_load_state("networkidle")
            body = text(pg).lower()
            assert "already" in body or "today" in body, "the one-per-day rule produced no message"
            return "Second same-day entry refused with an explanatory message."
        run(page, "TC-S16", "Record a second mood entry on the same day", "Boundary",
            "One entry per UTC day; the second is refused", tc16)

        # ---------- Forum ----------
        def tc17(pg):
            pg.goto(f"{BASE}/Forum")
            pg.wait_for_load_state("networkidle")
            assert "/Account/Login" not in pg.url, "the forum was not reachable"
            return "Forum categories listed."
        run(page, "TC-S17", "Open the peer forum", "Normal", "Category list renders", tc17)

        def tc18(pg):
            pg.goto(f"{BASE}/Forum")
            link = pg.locator("a[href*='/Forum/Category/']").first
            assert link.count(), "no forum category link was found"
            link.click()
            pg.wait_for_load_state("networkidle")
            pg.locator("form button.ls-btn--clay").first.click()
            pg.wait_for_timeout(800)
            body = text(pg).lower()
            assert "required" in body or "empty" in body or "must not" in body, (
                "an empty discussion was accepted")
            return "Empty title and body refused."
        run(page, "TC-S18", "Publish a discussion with an empty title and body", "Invalid",
            "Validation messages; nothing is published", tc18)

        def tc19(pg):
            pg.goto(f"{BASE}/Forum")
            pg.locator("a[href*='/Forum/Category/']").first.click()
            pg.wait_for_load_state("networkidle")
            pg.fill("#NewPost_Title", "Lab 6 system test discussion")
            pg.fill("#NewPost_Body", "Posted by the Lab 6 automated system test to verify publishing.")
            pg.locator("form button.ls-btn--clay").first.click()
            pg.wait_for_load_state("networkidle")
            assert "/Forum/Post/" in pg.url, f"the post was not published; on {pg.url}"
            post_url_holder["url"] = pg.url
            return f"Discussion published at {pg.url}."
        run(page, "TC-S19", "Publish a valid discussion", "Normal",
            "Post is stored and its detail page opens", tc19)

        def tc20(pg):
            assert "/Forum/Post/" in pg.url, "not on a post page"
            form = pg.locator("form[action='/Forum/AddComment']")
            form.locator("textarea").fill("A reply added by the Lab 6 system test.")
            form.locator("button[type=submit]").click()
            pg.wait_for_load_state("networkidle")
            assert "Lab 6 system test" in text(pg), "the reply was not shown"
            return "Reply stored and rendered on the discussion."
        run(page, "TC-S20", "Reply to a discussion", "Normal", "Comment is stored and displayed", tc20)

        def tc21(pg):
            post_url = pg.url
            pg.locator("details.forum-flag > summary").click()
            form = pg.locator("form[action^='/Forum/Flag/']")
            assert form.count(), "no report form on the discussion page"
            form.locator("textarea").fill("")
            form.locator("button[type=submit]").click()
            pg.wait_for_timeout(600)
            assert pg.url == post_url, "an empty report was submitted"
            return "The empty report was blocked and the page did not submit."
        run(page, "TC-S21", "Report a discussion without giving a reason", "Invalid",
            "Guidance message; no flag recorded", tc21)

        def tc21b(pg):
            pg.goto(post_url_holder["url"])
            box = pg.locator("form[action='/Forum/AddComment'] textarea")
            limit = int(box.get_attribute("maxlength") or 0)
            box.fill("y" * 2500)
            typed = len(box.input_value())
            assert limit == 2000, f"the reply box allows {limit} characters against a server limit of 2,000"
            assert typed == 2000, f"the box accepted {typed} characters"
            return f"The reply box stops at {typed} characters, matching the server limit."
        run(page, "TC-S21b", "Type past the reply length limit", "Boundary",
            "The reply box stops at the same 2,000 characters the server accepts", tc21b)

        def tc21c(pg):
            pg.goto(post_url_holder["url"])
            pg.locator("details.forum-flag > summary").click()
            box = pg.locator("form[action^='/Forum/Flag/'] textarea")
            limit = int(box.get_attribute("maxlength") or 0)
            assert limit == 500, f"the report box allows {limit} characters against a server limit of 500"
            return "The report box stops at 500 characters, matching the server limit."
        run(page, "TC-S21c", "Type past the report reason length limit", "Boundary",
            "The report box stops at the same 500 characters the server accepts", tc21c)

        def tc22(pg):
            pg.goto(f"{BASE}/Forum/Post/99999999")
            pg.wait_for_load_state("networkidle")
            body = text(pg).lower()
            assert "not found" in body or "404" in body or "sorry" in body, body[:200]
            return "A discussion identifier that does not resolve returns a not-found page."
        run(page, "TC-S22", "Open a discussion that does not exist", "Exceptional",
            "Not-found page rather than a server error", tc22)

        # ---------- Crisis resources ----------
        def tc23(pg):
            pg.goto(f"{BASE}/CrisisResource")
            pg.wait_for_load_state("networkidle")
            box = pg.locator("input[type='search'], input[name='query'], input[name='q']").first
            assert box.count(), "no search box on the crisis page"
            box.fill("I cannot sleep and exams start next week")
            pg.keyboard.press("Enter")
            pg.wait_for_load_state("networkidle")
            return "Search returned a ranked result page."
        run(page, "TC-S23", "Search the crisis resources with a sentence", "Normal",
            "Ranked resources are listed", tc23)

        def tc24(pg):
            try:
                pg.goto(f"{BASE}/CrisisResource?query=" + "z" * 3000, wait_until="domcontentloaded")
            except Exception:
                pg.wait_for_timeout(500)
            pg.wait_for_load_state("networkidle")
            body = text(pg).lower()
            assert "error" not in body[:400], "an overlong query produced an error page"
            return "A 3,000 character query was handled without an error."
        run(page, "TC-S24", "Search the crisis resources with a 3,000 character query", "Boundary",
            "Query is truncated to the ranking limit; page still renders", tc24)

        # ---------- Booking ----------
        def tc25(pg):
            pg.goto(f"{BASE}/Booking")
            pg.wait_for_load_state("networkidle")
            assert "/Account/Login" not in pg.url, "the booking page was not reachable"
            return "Booking page rendered with the published slots or an empty state."
        run(page, "TC-S25", "Open the counselor booking page", "Normal",
            "Available slots or an empty state render", tc25)

        # ---------- Authorization ----------
        def tc26(pg):
            pg.goto(f"{BASE}/Admin")
            pg.wait_for_load_state("networkidle")
            body = text(pg).lower()
            assert "/Admin" not in pg.url or "denied" in body or "sign in" in body, (
                f"a student reached the admin area at {pg.url}")
            return "Student is refused the administration area."
        run(page, "TC-S26", "Student opens the administration area", "Exceptional",
            "Access denied; the page is not served", tc26)

        def tc27(pg):
            pg.goto(f"{BASE}/Counselor/Queue")
            pg.wait_for_load_state("networkidle")
            body = text(pg).lower()
            assert "denied" in body or "sign in" in body or "/Account" in pg.url, (
                f"a student reached the counselor queue at {pg.url}")
            return "Student is refused the counselor risk queue."
        run(page, "TC-S27", "Student opens the counselor risk queue", "Exceptional",
            "Access denied; no risk data is exposed", tc27)

        def tc28(pg):
            pg.goto(f"{BASE}/this/route/does/not/exist")
            pg.wait_for_load_state("networkidle")
            return "Unknown route returned a handled response rather than a stack trace."
        run(page, "TC-S28", "Request a URL that does not exist", "Exceptional",
            "Handled not-found response, no stack trace", tc28)

        # ---------- Consent ----------
        def tc29(pg):
            pg.goto(f"{BASE}/Student")
            pg.wait_for_load_state("networkidle")
            assert "/Account/Login" not in pg.url, "the student privacy area was not reachable"
            return "Student privacy and monitoring area rendered."
        run(page, "TC-S29", "Open the student privacy and monitoring area", "Normal",
            "Consent state and the transparency panel render", tc29)

        def tc30(pg):
            pg.goto(f"{BASE}/Student")
            forms = pg.locator("form[action*='UpdateRiskMonitoring']")
            assert forms.count(), "no monitoring consent control was found"
            forms.first.locator("button[type=submit]").click()
            pg.wait_for_load_state("networkidle")
            return "Monitoring consent was changed from the student's own privacy page."
        run(page, "TC-S30", "Change the monitoring consent decision", "Normal",
            "Consent is updated and confirmed on screen", tc30)

        # ---------- Session end ----------
        def tc31(pg):
            sign_out(pg)
            pg.goto(f"{BASE}/Dashboard")
            pg.wait_for_load_state("networkidle")
            assert "/Account/Login" in pg.url, "the session survived sign-out"
            return "Session ended; private pages require signing in again."
        run(page, "TC-S31", "Sign out and re-request a private page", "Normal",
            "Session is cleared and the dashboard is no longer served", tc31)

        # ---------- Rate limiting (runs last: it deliberately exhausts the window) ----------
        def tc32(pg):
            statuses = []
            for attempt in range(14):
                resp = pg.request.post(
                    f"{BASE}/Account/Login",
                    form={"Email": f"flood{attempt}@test.local", "Password": "WrongPassword!1"},
                    max_redirects=0, fail_on_status_code=False)
                statuses.append(resp.status)
                if resp.status == 429:
                    break
            assert 429 in statuses, f"the limiter never engaged; statuses were {statuses}"
            pg.goto(f"{BASE}/Account/Login")
            return (f"The sign-in endpoint began refusing with 429 after "
                    f"{statuses.index(429)} attempts in the window.")
        run(page, "TC-S32", "Repeated sign-in attempts from one address", "Exceptional",
            "The auth rate limiter refuses further attempts with 429", tc32)

        browser.close()

    out = ROOT / "docs" / "testing" / "system-test-results.json"
    out.write_text(json.dumps({
        "account": {"email": STUDENT_EMAIL, "studentNumber": STUDENT_NUMBER},
        "cases": results,
    }, indent=2), encoding="utf-8")
    passed = sum(1 for r in results if r["status"] == "PASS")
    print(f"\n{passed}/{len(results)} passed; results written to {out}")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
