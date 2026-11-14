"""Edit-summary parser, Finnish revert-reason lexicon, and insert-summary flags (P1.S5, #32).

Pure functions only — no database or network access. Built from the corpus patterns
profiled in `research/profiling/prof_ed/findings.md` §4 and live `corpus.revision` samples.
"""

from __future__ import annotations

import re

FAMILIES: tuple[str, ...] = (
    "rollback",
    "undo_modern",
    "flaggedrevs",
    "undo_legacy",
    "popups_en",
    "legacy_rollback",
)

REASON_CATEGORIES: tuple[str, ...] = (
    "vandalism_test",
    "sourcing",
    "neutrality_pov",
    "factual_error",
    "original_research",
    "block_evasion",
    "copyright",
    "maintenance",
    "consensus",
)

INSERT_FLAGS: tuple[str, ...] = (
    "empty_large",
    "minor_label_large",
    "argues_position",
    "cites_source",
)

# Alphabetic word boundaries (Unicode-aware, digits/underscores/hyphens do not block boundaries).
_WB_L = r"(?<![^\W\d_])"
_WB_R = r"(?![^\W\d_])"

_SECTION_RE = re.compile(r"^(?:\s*/\*.*?\*/\s*)+", re.DOTALL)

# User expression inside MediaWiki auto-summaries: wikilink or plain user/IP token.
_USER_LINK_RE = re.compile(
    r"^\[\[:?(?:(?:Special:Contributions|Toiminnot:(?:Muokkaukset|Contributions))/([^|\]]+)"
    r"|(?:User|Käyttäjä):([^|\]]+)"
    r"|([^|\]]+))(?:\|([^\]]*))?\]\]$",
    re.IGNORECASE,
)
_TALK_PAREN = r"(?:\s*\((?:\[\[:?(?:User[ _]talk|Keskustelu[ _]käyttäjästä)[^\]]*\]\]|keskustelu|talk)\))?"
_USER_TOKEN = r"(?:\[\[[^\]]+\]\]|[^\s()\[\],:;]+)"

# Gadget suffixes that are tool metadata rather than human-written reasons.
_GADGET_SUFFIX_RE = re.compile(
    r"(?:\s*\((?:MobileUndo\s+v[\d.]+|TwinkleGlobal|\[\[:?(?:meta|metawiki):[^\]]+\]\])\))+\s*$",
    re.IGNORECASE,
)

# 1. MediaWiki Rollback (incl. 2006-2007 `<user>:n`, vandalism-rollback gadget & 255-byte truncated summaries)
_ROLLBACK_FULL_RE = re.compile(
    rf"^(?P<pre>.*?\s)?(?:Käyttäjän\s+(?P<user1>{_USER_TOKEN})|(?P<user2>{_USER_TOKEN}):n){_TALK_PAREN}\s+"
    r"(?:muokkaukset|vandalismi)\s+kumottiin\s+ja\s+(?:sivu|artikkeli)\s+palautettiin\s+"
    rf"(?:viimeisimpään\s+)?(?:käyttäjän\s+{_USER_TOKEN}|.+?:n)\s+tekemään\s+versioon(?:\s+(?P<rev>\d+))?\.?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_ROLLBACK_TRUNC_RE = re.compile(
    rf"^(?P<pre>.*?\s)?(?:Käyttäjän\s+(?P<user1>{_USER_TOKEN})|(?P<user2>{_USER_TOKEN}):n){_TALK_PAREN}\s+"
    r"(?:muokkaukset|vandalismi)\s+kumottiin\s+ja\s+(?:sivu|artikkeli)\s+pal.*$",
    re.IGNORECASE,
)

# 2. Modern MediaWiki Undo: "Kumottu muokkaus <id>, jonka teki ..."
_UNDO_REV = r"(?:#?(?P<rev1>\d+)|\[\[:?(?:Special:Diff|Toiminnot:Ero)/(?P<rev2>\d+)(?:\|[^\]]*)?\]\])"
_UNDO_MODERN_RE = re.compile(
    rf"^(?P<pre>.*?)Kumottu\s+muokkaus\s+{_UNDO_REV}"
    rf"(?:,\s*jonka\s+teki\s+(?P<user>{_USER_TOKEN}){_TALK_PAREN})?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)

# 3. FlaggedRevs Reject: "Hylättiin viimeisin/viimeisimmät/ensimmäinen ... tekstimuutos ..."
_FLAGGEDREVS_PREFIX_RE = re.compile(
    r"^(?P<pre>.*?)(?:(?<=^)|(?<=[\s.!?;:]))"
    r"Hylättiin\s+(?:viimeisin|viimeisimmät|viimeiset|viimeinen|ensimmäinen|ensimmäiset)\s+"
    r"(?:\d+\s+)?tekstimuuto\w*(?:\s+sotkemisena)?\s*"
    r"(?:\((?:tehnyt|tehneet|käyttäjältä)\s+(?P<users>.+?)\)\s*)?"
    r"(?:(?:ja\s+palautettiin\s+versio|,\s*jot?ka\s+tehtiin\s+versioon)\s+(?P<rev>\d+)"
    r"(?:(?:,\s*jonka\s+on\s+tehnyt|\s+käyttäjältä)\s+(?P<rest>.*)|(?P<ver_tail>.*))"
    r"|(?P<direct_tail>.*))$",
    re.IGNORECASE | re.DOTALL,
)
_RESTORED_SELF_DELIM_RE = re.compile(
    r"^(?:\[\[[^\]]+\]\]"
    r"|[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{0,4}){2,7}"
    r"|\d{1,3}(?:\.\d{1,3}){3})(?P<tail>.*)$",
    re.DOTALL,
)
_RESTORED_PUNCT_SPLIT_RE = re.compile(
    r"^[^:;]+?(?=(?::\s|;\s|,\s|-->|\s-\s|\s\(|[:;]$))(?P<tail>.*)$",
    re.DOTALL,
)

# 4. Legacy MediaWiki Undo: "Käyttäjän X muokkaus <id> kumottiin" / "Käyttäjän X muokkaukset kumottiin"
_UNDO_LEGACY_USER_REV_RE = re.compile(
    rf"^(?P<pre>.*?\s)?Käyttäjän\s+(?P<user>{_USER_TOKEN}){_TALK_PAREN}\s+"
    r"muokkaus\s+(?P<rev>\d+)\s+kumottiin(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_UNDO_LEGACY_BARE_REV_RE = re.compile(
    r"^muokkaus\s+(?P<rev>\d+)\s+kumottiin(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_UNDO_LEGACY_NO_REV_RE = re.compile(
    rf"^Käyttäjän\s+(?P<user>{_USER_TOKEN}){_TALK_PAREN}\s+"
    r"(?:[\wÄÖÅäöå]+\s+){0,3}muokkaukset\s+kumottiin(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)

# 5. Navigation Popups / EN gadgets (Popups, TwinkleGlobal, English Undid revision)
_POPUPS_PRIOR_RE = re.compile(
    r"^Revert(?:ed)?\s+to\s+the\s+revision\s+prior\s+to\s+revision\s+(?P<rev>\d+)"
    r"(?:\s+dated\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})?"
    rf"(?:\s+by\s+(?P<user>{_USER_TOKEN}))?"
    r"(?:\s+using\s+\[\[[^\]]+\]\])?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_POPUPS_REV_RE = re.compile(
    r"^Revert(?:ed)?\s+to\s+revision\s+(?P<rev>\d+)"
    r"(?:\s+dated\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})?"
    r"(?:\s+by\s+.+?(?=\s+using\s+\[\[|[:;(]|$))?"
    r"(?:\s+using\s+\[\[[^\]]+\]\])?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_EN_REVERTED_BY_RE = re.compile(
    rf"^Reverted\s+(?:\d+\s+)?edits?\s+by\s+(?P<user>{_USER_TOKEN}){_TALK_PAREN}"
    r"(?:\s+to\s+last\s+revision\s+by\s+[^:;()]+)?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_EN_UNDID_RE = re.compile(
    rf"^Undid\s+revision\s+(?P<rev>\d+)\s+by\s+(?P<user>{_USER_TOKEN}){_TALK_PAREN}(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)

# 6. Legacy Rollback (2004–2006): "Palautettiin/Palautettu ... muutokseen"
_LEGACY_RB_KAYTTAJAN_RE = re.compile(
    rf"^Käyttäjän\s+(?P<user>{_USER_TOKEN}){_TALK_PAREN}\s+muokkaukset\s+palautettiin\s+"
    r"(?:viimeisimpään|viimeiseen)\s+käyttäjän\s+.+?\s+tekemään\s+muutokseen\.?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_LEGACY_RB_GEN_RE = re.compile(
    r"^Palautettiin\s+(?P<user>.+?):n\s+muokkaukset\s+"
    r"(?:viimeisimpään|viimeiseen)\s+.+?\s+tekemään\s+muutokseen\.?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)
_LEGACY_RB_PLAIN_RE = re.compile(
    r"^Palautett(?:iin|u)\s+.+?\s+muutokseen\.?(?P<tail>.*)$",
    re.IGNORECASE | re.DOTALL,
)


def strip_section(comment: str | None) -> str:
    """Remove leading `/* section */` markers and surrounding whitespace."""
    if not comment:
        return ""
    return _SECTION_RE.sub("", comment).strip()


def _extract_user(token: str | None) -> str | None:
    if not token:
        return None
    t = token.strip()
    if not t:
        return None
    m = _USER_LINK_RE.match(t)
    if m:
        raw = m.group(1) or m.group(2) or m.group(4) or m.group(3)
        return raw.strip().replace("_", " ") if raw else None
    return t


def _extract_first_user_from_list(users_blob: str | None) -> str | None:
    if not users_blob:
        return None
    blob = users_blob.strip()
    link = re.match(r"^\[\[[^\]]+\]\]", blob)
    if link:
        return _extract_user(link.group(0))
    first = re.split(r"\s+ja\s+|,\s*", blob, maxsplit=1)[0]
    return _extract_user(first)


def _clean_tail(tail: str | None, pre: str | None = None) -> str:
    parts: list[str] = []
    if pre:
        p = pre.strip()
        p = re.sub(r"[:;,.\-–—\s]+$", "", p).strip()
        if p:
            parts.append(p)
    if tail:
        t = _GADGET_SUFFIX_RE.sub("", tail).strip()
        t = re.sub(r"^(?:-->|[:;,.\-–—\s])+", "", t).strip()
        # Unwrap single outer parentheses `(reason)` when balanced across the whole tail.
        if t.startswith("(") and t.endswith(")"):
            inner = t[1:-1].strip()
            depth = 0
            balanced = True
            for ch in inner:
                if ch == "(":
                    depth += 1
                elif ch == ")":
                    depth -= 1
                    if depth < 0:
                        balanced = False
                        break
            if balanced and depth == 0:
                t = inner
        t = _GADGET_SUFFIX_RE.sub("", t).strip()
        if t:
            parts.append(t)
    return " ".join(parts).strip()


def parse_auto(comment: str | None) -> dict[str, object] | None:
    """Parse MediaWiki / FlaggedRevs / gadget auto-revert summaries (6 families).

    Strips `/* section */` first. Returns `{family, target_rev_id, reverted_user, reason_text}`
    or `None` when the summary does not match an auto-revert template.
    """
    body = strip_section(comment)
    if not body:
        return None

    # 1. MediaWiki Rollback
    m = _ROLLBACK_FULL_RE.match(body)
    if m:
        rev = int(m.group("rev")) if m.group("rev") else None
        return {
            "family": "rollback",
            "target_rev_id": rev,
            "reverted_user": _extract_user(m.group("user1") or m.group("user2")),
            "reason_text": _clean_tail(m.group("tail"), m.group("pre")),
        }
    m = _ROLLBACK_TRUNC_RE.match(body)
    if m:
        return {
            "family": "rollback",
            "target_rev_id": None,
            "reverted_user": _extract_user(m.group("user1") or m.group("user2")),
            "reason_text": _clean_tail(None, m.group("pre")),
        }

    # 2. Modern MediaWiki Undo
    m = _UNDO_MODERN_RE.match(body)
    if m:
        rev_s = m.group("rev1") or m.group("rev2")
        return {
            "family": "undo_modern",
            "target_rev_id": int(rev_s),
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail"), m.group("pre")),
        }

    # 3. FlaggedRevs Reject
    m = _FLAGGEDREVS_PREFIX_RE.match(body)
    if m:
        rest = m.group("rest")
        tail = ""
        if rest is not None:
            rest_s = rest.strip()
            if rest_s:
                sd = _RESTORED_SELF_DELIM_RE.match(rest_s)
                if sd:
                    tail = sd.group("tail")
                else:
                    ps = _RESTORED_PUNCT_SPLIT_RE.match(rest_s)
                    if ps:
                        tail = ps.group("tail")
        else:
            tail = m.group("ver_tail") or m.group("direct_tail") or ""
        rev_id = int(m.group("rev")) if m.group("rev") else None
        return {
            "family": "flaggedrevs",
            "target_rev_id": rev_id,
            "reverted_user": _extract_first_user_from_list(m.group("users")),
            "reason_text": _clean_tail(tail, m.group("pre")),
        }

    # 4. Legacy MediaWiki Undo
    m = _UNDO_LEGACY_USER_REV_RE.match(body)
    if m:
        return {
            "family": "undo_legacy",
            "target_rev_id": int(m.group("rev")),
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail"), m.group("pre")),
        }
    m = _UNDO_LEGACY_BARE_REV_RE.match(body)
    if m:
        return {
            "family": "undo_legacy",
            "target_rev_id": int(m.group("rev")),
            "reverted_user": None,
            "reason_text": _clean_tail(m.group("tail")),
        }
    m = _UNDO_LEGACY_NO_REV_RE.match(body)
    if m:
        return {
            "family": "undo_legacy",
            "target_rev_id": None,
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail")),
        }

    # 5. Navigation Popups / EN gadget
    m = _POPUPS_PRIOR_RE.match(body)
    if m:
        return {
            "family": "popups_en",
            "target_rev_id": int(m.group("rev")),
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail")),
        }
    m = _POPUPS_REV_RE.match(body)
    if m:
        return {
            "family": "popups_en",
            "target_rev_id": int(m.group("rev")),
            "reverted_user": None,
            "reason_text": _clean_tail(m.group("tail")),
        }
    m = _EN_REVERTED_BY_RE.match(body)
    if m:
        return {
            "family": "popups_en",
            "target_rev_id": None,
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail")),
        }
    m = _EN_UNDID_RE.match(body)
    if m:
        return {
            "family": "popups_en",
            "target_rev_id": int(m.group("rev")),
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail")),
        }

    # 6. Legacy Rollback (2004–2006)
    m = _LEGACY_RB_KAYTTAJAN_RE.match(body)
    if m:
        return {
            "family": "legacy_rollback",
            "target_rev_id": None,
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail")),
        }
    m = _LEGACY_RB_GEN_RE.match(body)
    if m:
        return {
            "family": "legacy_rollback",
            "target_rev_id": None,
            "reverted_user": _extract_user(m.group("user")),
            "reason_text": _clean_tail(m.group("tail")),
        }
    m = _LEGACY_RB_PLAIN_RE.match(body)
    if m:
        return {
            "family": "legacy_rollback",
            "target_rev_id": None,
            "reverted_user": None,
            "reason_text": _clean_tail(m.group("tail")),
        }

    return None


def extract_reason_text(comment: str | None) -> str:
    """Return the human-written reason text for any edit summary (auto or manual)."""
    auto = parse_auto(comment)
    if auto is not None:
        return str(auto["reason_text"])
    return strip_section(comment)


# ---------------------------------------------------------------------------
# Finnish + English reason lexicon (9 categories)
# ---------------------------------------------------------------------------

_IABOT_RE = re.compile(r"#iabot\b", re.IGNORECASE)

_VANDALISM_RE = re.compile(
    rf"{_WB_L}(?:"
    r"vandalis\w*|vandaal\w*|vandal\w*"
    r"|sotk(?:u|e|i|enta|enna)\w*"
    r"|häirik\w*"
    r"|hiekkalaatik\w*"
    r"|testimuokkau\w*|testail\w*|testi"
    r"|kokeil\w*"
    r"|töhry\w*|töhri\w*"
    r"|hölynpöly\w*|pelleily\w*|pilail\w*|hassuttel\w*"
    r"|asiaton\w*|asiattom\w*|epäasialli\w*"
    r"|späm\w*|roskapost\w*"
    r"|rvv|spam\w*|nonsense|gibberish|graffiti"
    rf"){_WB_R}"
    r"|sivu\s+tyhjennettiin|tyhjensi\s+sivun|sivun\s+tyhjennys",
    re.IGNORECASE,
)
_ROSKA_RE = re.compile(rf"{_WB_L}roska(?!luok)\w*{_WB_R}", re.IGNORECASE)
_EI_ROSKAA_RE = re.compile(rf"{_WB_L}ei(?:kä)?\s+(?:ole\s+)?roska\w*{_WB_R}", re.IGNORECASE)

_SOURCING_RE = re.compile(
    rf"{_WB_L}(?:"
    r"lähteetön\w*|lähteettöm\w*"
    r"|viitteetön\w*|viitteettöm\w*"
    r"|lähdepyynt\w*"
    r"|unsourced|unreferenced"
    rf"){_WB_R}"
    rf"|{_WB_L}(?:tieto)?lähde\s*\?+"
    rf"|{_WB_L}(?:ei|ilman)\s+(?:\w+\s+)?(?:tieto)?läh(?:dettä|teitä){_WB_R}"
    rf"|{_WB_L}(?:(?:tieto)?lähde|lähteet|viite|viitteet)\s+puuttu\w*{_WB_R}"
    rf"|{_WB_L}(?:vaatii|kaipaa|tarvitsee|etsi)\s+(?:tieto)?läh(?:teen|dettä|de){_WB_R}"
    rf"|{_WB_L}lähdeaineisto\s+on\s+puutteelli\w*{_WB_R}"
    rf"|{_WB_L}ei\s+(?:ole\s+|mainita\s+|löydy\s+|sanota\s+|väitetä\s+)?lähtee(?:ssä|stä){_WB_R}"
    rf"|{_WB_L}(?:lähteen\s+vastai\w*|vastoin\s+lähdettä){_WB_R}"
    rf"|{_WB_L}(?:ei\s+(?:ole\s+|vastaa\s+)?lähteen\s*mukai\w*|ei\s+vastaa\s+(?:\w+\s+)?lähdettä){_WB_R}"
    rf"|{_WB_L}(?:vastaakohan\s+(?:\w+\s+)?lähdettä|onkohan\s+lähteen\s*mukai\w*){_WB_R}"
    rf"|{_WB_L}(?:kelvoton|epäluotettava|huono|heikko)\s+(?:tieto)?lähde{_WB_R}"
    rf"|{_WB_L}lähteistetyn\s+tiedon\s+poisto{_WB_R}"
    rf"|{_WB_L}(?:citation\s+needed|no\s+sources?|unreliable\s+sources?){_WB_R}",
    re.IGNORECASE,
)

_NEUTRALITY_POV_RE = re.compile(
    rf"{_WB_L}(?:"
    r"n?pov"
    r"|pr[oå]pagand\w*"
    r"|(?:epä)?puolueelli\w*"
    r"|(?:epä)?neutraal\w*|neutraloi\w*"
    r"|mielipi(?:de|te)\w*"
    r"|spekul(?:aatio|oint|oi)\w*"
    r"|asenteelli\w*"
    r"|näkökulm\w*"
    r"|väritty\w*|kantaaottav\w*|arvottav\w*|leimaav\w*|tendenssi\w*|pamflett\w*"
    r"|bias(?:ed|ia)?|non-neutral|one-sided"
    rf"){_WB_R}",
    re.IGNORECASE,
)

_FACTUAL_ERROR_RE = re.compile(
    rf"{_WB_L}(?:"
    r"väär(?:ä\w*|i\w*|in)"
    r"|(?:asia|fakta)?virhe\w*"
    r"|valhe\w*"
    r"|paikkansapitämät\w*"
    r"|perätön\w*|perättöm\w*"
    r"|epätos\w*|epätot\w*"
    r"|pöty\w*|puppu\w*|huuhaa\w*"
    r"|fakta(?:t|a|n)?"
    r"|false|untrue|inaccurate|incorrect|hoax|fabricated"
    rf"){_WB_R}"
    rf"|{_WB_L}ei(?:vät)?\s+pidä\s+paikkaansa{_WB_R}"
    rf"|{_WB_L}vastoin\s+tosiasioita{_WB_R}"
    rf"|{_WB_L}factual\s+errors?{_WB_R}",
    re.IGNORECASE,
)
_GRAMMAR_VAARIN_RE = re.compile(rf"{_WB_L}kielellisesti\s+väärin{_WB_R}", re.IGNORECASE)

_ORIGINAL_RESEARCH_RE = re.compile(
    rf"{_WB_L}oma\w*\s+tutkimu\w*{_WB_R}"
    rf"|{_WB_L}(?:uutta|uusi)\s+tutkimu\w*{_WB_R}"
    rf"|{_WB_L}oma\w*\s+(?:päätelm\w*|tulkint\w*|pohdint\w*){_WB_R}"
    rf"|{_WB_L}(?:wp:n?or|original\s+research){_WB_R}",
    re.IGNORECASE,
)

_BLOCK_EVASION_RE = re.compile(
    rf"{_WB_L}(?:"
    r"eston\s*kier\w*"
    r"|estet(?:ty|yn)\s+käyttäjä\w*"
    r"|sukkanukk\w*"
    r"|sockpuppet\w*"
    r"|block\s+evasion"
    rf"){_WB_R}"
    rf"|{_WB_L}(?:rv|palautus)[,\s–\-]+wp:?k{_WB_R}",
    re.IGNORECASE,
)

_COPYRIGHT_RE = re.compile(
    rf"{_WB_L}(?:"
    r"tekijänoikeu\w*"
    r"|copyvio\w*|copyright\w*"
    r"|kopio(?:itu|intia|inti)\w*"
    r"|plagiaat\w*|plagioi\w*"
    rf"){_WB_R}"
    rf"|{_WB_L}(?:suora\w*|luvaton\w*)\s+kopio\w*{_WB_R}",
    re.IGNORECASE,
)
_COPY_TEMPLATE_OK_RE = re.compile(
    r"tekijänoikeus(?:tsekkaus)?malline\s+pois|\{\{tekijänoikeus_",
    re.IGNORECASE,
)

_MAINTENANCE_RE = re.compile(
    rf"{_WB_L}(?:"
    r"format\w*|muotoil\w*|ulkoasu\w*|asettel\w*|tyyliopa\w*"
    r"|kielenhuol\w*|kh|oikolu\w*|kieliasu\w*|oikeinkirjoitu\w*|kielellisesti"
    r"|typo\w*|(?:kirjoitus|lyönti|paino|näppäily|pilkku|kieli(?:oppi)?)virhe\w*"
    r"|(?:wiki|kieli)?link(?:ki|it|kejä|kien|itys|itystä|in)\w*|interwiki\w*|wikity\w*"
    r"|(?:pää|ala|ylä|tupla|paikkakunta|roska)?luok(?:ka|at|itus|ittelu\w*|asta|kaan|kia)\w*|hotcat\w*"
    r"|malline\w*|tietolaatik\w*|navigaatio\w*|täsmennyssiv\w*|defaultsort"
    r"|wl|lk|iw|fix|cleanup"
    rf"){_WB_R}"
    rf"|(?<!\S)(?:-?w|-?l)(?!\S)",
    re.IGNORECASE,
)

_CONSENSUS_RE = re.compile(
    rf"{_WB_L}(?:"
    r"konsensu\w*"
    r"|keskustelusivu\w*"
    r"|kahvihuone\w*"
    r"|poistoäänesty\w*"
    r"|consensus"
    rf"){_WB_R}"
    rf"|{_WB_L}(?:per\.?|ks\.?|kts\.?|katso)\s+keskustelu\w*{_WB_R}"
    rf"|{_WB_L}keskustelun\s+(?:perusteella|mukaan){_WB_R}"
    rf"|{_WB_L}ilman\s+keskustelua{_WB_R}"
    rf"|{_WB_L}per\s+talk{_WB_R}",
    re.IGNORECASE,
)


def reasons(text: str | None) -> set[str]:
    """Classify human-written summary text into zero or more of the 9 reason categories."""
    if not text:
        return set()
    t = strip_section(text)
    if not t or _IABOT_RE.search(t):
        return set()

    out: set[str] = set()

    if _VANDALISM_RE.search(t) or (_ROSKA_RE.search(t) and not _EI_ROSKAA_RE.search(t)):
        out.add("vandalism_test")

    if _SOURCING_RE.search(t):
        out.add("sourcing")

    if _NEUTRALITY_POV_RE.search(t):
        out.add("neutrality_pov")

    t_no_gram = _GRAMMAR_VAARIN_RE.sub("", t)
    if _FACTUAL_ERROR_RE.search(t_no_gram):
        out.add("factual_error")

    if _ORIGINAL_RESEARCH_RE.search(t):
        out.add("original_research")

    if _BLOCK_EVASION_RE.search(t):
        out.add("block_evasion")

    t_no_copy_ok = _COPY_TEMPLATE_OK_RE.sub("", t)
    if _COPYRIGHT_RE.search(t_no_copy_ok):
        out.add("copyright")

    if _MAINTENANCE_RE.search(t) or _COPY_TEMPLATE_OK_RE.search(t):
        out.add("maintenance")

    if _CONSENSUS_RE.search(t):
        out.add("consensus")

    return out


# ---------------------------------------------------------------------------
# Insert-summary flags
# ---------------------------------------------------------------------------

_MINOR_ONLY_RE = re.compile(
    r"^[\s,.;:+\-()/]*(?:"
    r"typo\w*|kh|kielenhuol\w*|korjau\w*|korjattu|pikkukorjau\w*|pikku\s+korjau\w*"
    r"|pieni\w*(?:\s+(?:muuto\w*|korjau\w*|lisäy\w*|muokkau\w*|viilau\w*))?"
    r"|(?:kirjoitus|lyönti|paino|pilkku)virhe\w*"
    r"|fix|f|p"
    r")(?:[\s,.;:+/\-]+(?:"
    r"typo\w*|kh|kielenhuol\w*|korjau\w*|korjattu|pieni\w*|fix"
    r"))*[\s,.;:+\-()]*$",
    re.IGNORECASE,
)

_ARGUES_WORDS_RE = re.compile(
    rf"{_WB_L}(?:"
    r"totuus|totuuden|totuutta|totuudesta"
    r"|oikeasti"
    r"|todellisuudessa|tosiasiassa"
    rf"){_WB_R}",
    re.IGNORECASE,
)
_HTML_COMMENT_OR_URL_RE = re.compile(r"<!--.*?-->|https?://\S+", re.DOTALL | re.IGNORECASE)
_EXCLAMATION_RE = re.compile(r"""[A-Za-zÄÖÅäöå0-9)"'»]\s*!""")

_CITES_URL_OR_REF_RE = re.compile(
    rf"https?://|www\.|<ref[\s>/]|{_WB_L}(?:refs?|isbn){_WB_R}",
    re.IGNORECASE,
)
_CITES_LAHDE_VIITE_RE = re.compile(
    rf"{_WB_L}(?:(?:tieto|verkko|kirja)?läh(?:de|teen|teet|teitä|teestä|teessä|teeseen|teellä|teiden|deviit\w*)"
    rf"|viit(?:e|teen|teet|teitä|teist\w*)){_WB_R}",
    re.IGNORECASE,
)
_NEGATED_SOURCE_RE = re.compile(
    rf"{_WB_L}(?:ei|ilman)\s+(?:\w+\s+)?(?:tieto)?läh(?:dettä|teitä){_WB_R}"
    rf"|{_WB_L}(?:(?:tieto)?lähde|lähteet|viite|viitteet)\s+puuttu\w*{_WB_R}"
    rf"|{_WB_L}(?:tieto)?lähde\s*\?+",
    re.IGNORECASE,
)


def insert_flags(
    comment: str | None,
    bytes_delta: int | None,
    minor: bool | int = False,
) -> set[str]:
    """Compute insert-summary flags for an edit given its comment, byte delta, and minor flag."""
    # Auto-generated revert templates describe removals (and MediaWiki forces minor=1 on rollbacks).
    if parse_auto(comment) is not None:
        return set()

    body = strip_section(comment)
    large = bytes_delta is not None and abs(bytes_delta) >= 500
    flags: set[str] = set()

    if not body:
        if large:
            flags.add("empty_large")
            if bool(minor):
                flags.add("minor_label_large")
        return flags

    if _IABOT_RE.search(body):
        return flags

    if large and (bool(minor) or _MINOR_ONLY_RE.match(body) is not None):
        flags.add("minor_label_large")

    cats = reasons(body)
    cleaned_for_bang = _HTML_COMMENT_OR_URL_RE.sub(" ", body)
    if (
        "neutrality_pov" in cats
        or "factual_error" in cats
        or _ARGUES_WORDS_RE.search(body)
        or _EXCLAMATION_RE.search(cleaned_for_bang)
    ):
        flags.add("argues_position")

    body_no_neg = _NEGATED_SOURCE_RE.sub(" ", body)
    if _CITES_URL_OR_REF_RE.search(body) or _CITES_LAHDE_VIITE_RE.search(body_no_neg):
        flags.add("cites_source")

    return flags
