#!/usr/bin/env python3
"""Independent review of P1.S1 (#28) revert classes across all 140 sampled reverts (7 classes x 20).

Usage:
  python3 research/28-s1-revert/review/review.py                  # fetch <=300 texts, diff, write verdicts.jsonl & summary
  python3 research/28-s1-revert/review/review.py --inspect <cls>  # print masked diffs + history context for one S1 class
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import random
import re
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
S1_DIR = HERE.parent
REPO = S1_DIR.parents[1]
sys.path.insert(0, str(S1_DIR.parent / "lib"))
import aot  # noqa: E402

CACHE_DIR = pathlib.Path(os.environ.get("AOT_CACHE_DIR", pathlib.Path.home() / ".cache" / "aot-research")) / "s1-review"

S1_CLASSES = [
    "excluded",
    "bot",
    "self_revert",
    "edit_war",
    "dispute",
    "graffiti_cleanup",
    "other",
]

VALID_VERDICTS = [
    "graffiti",
    "good_faith_fix",
    "content_dispute",
    "edit_war",
    "self_correction",
    "bot_maintenance",
    "collision_artifact",
    "unclear",
]

# 1-to-1 target verdict for each S1 class (`other` holds non-vandalism, non-dispute fixes in S1's 7-class taxonomy).
EXPECTED_VERDICT_BY_CLASS: dict[str, str] = {
    "excluded": "collision_artifact",
    "bot": "bot_maintenance",
    "self_revert": "self_correction",
    "edit_war": "edit_war",
    "dispute": "content_dispute",
    "graffiti_cleanup": "graffiti",
    "other": "good_faith_fix",
}

# Recorded independent verdicts after reading all 140 Finnish WikiDiff outputs, masked summaries, and page histories.
# Keys: reverting_rev_id -> (verdict, neutral English note with no raw usernames, IPs, or wikitext).
VERDICTS: dict[int, tuple[str, str]] = {
    # --- excluded (20) ---
    9812: (
        "collision_artifact",
        "Anonymous 0-byte page blanking 28s after an unblanking; matches earlier empty-page SHA-1 rather than reverting the restoration.",
    ),
    134093: (
        "collision_artifact",
        "Anonymous 0-byte blanking of a redirect page colliding with an unrelated empty revision from 8 months earlier.",
    ),
    154919: (
        "collision_artifact",
        "Anonymous 0-byte blanking of a 4.9 KB biography colliding with an empty revision from 13 months earlier across 36 edits.",
    ),
    162374: (
        "edit_war",
        "Administrator rolled back 72 revisions to a pre-dispute version over thesis-plagiarism claims and protected the page; real edit-war rollback dropped by wide_span (>15).",
    ),
    1932937: (
        "edit_war",
        "Patroller rolled back repeated re-insertion of unsourced corporate text to a 20-byte redirect; real edit-war revert dropped by restored_bytes <= 100.",
    ),
    2258372: (
        "graffiti",
        "Editor removed fabricated village and person names added across 17 anonymous edits; real graffiti cleanup dropped by wide_span (span=18).",
    ),
    3344994: (
        "content_dispute",
        "Editor reverted 17 consecutive anonymous edits rewriting religious doctrine and criticism sections without valid sources; real content dispute dropped by wide_span (span=17).",
    ),
    3536148: (
        "collision_artifact",
        "Anonymous 0-byte page blanking of a 65 KB biography colliding with an empty revision from 12 months earlier across 350 edits.",
    ),
    3684750: (
        "edit_war",
        "Editor converted a 1.9 KB company article back into an 18-byte redirect during a back-and-forth redirect dispute; falsely excluded by restored_bytes <= 100.",
    ),
    4160250: (
        "collision_artifact",
        "Anonymous 0-byte page blanking of a 20 KB biography colliding with an empty revision from 34 days earlier across 38 edits.",
    ),
    4160257: (
        "collision_artifact",
        "Repeat anonymous 0-byte blanking 48s after patroller unblanking; matches empty-page SHA-1 and is rightly excluded so the unblanking is not marked reverted.",
    ),
    5779924: (
        "edit_war",
        "Mutual revert replacing a 2.4 KB historical society article with a 36-byte redirect over organizational continuity; falsely excluded by restored_bytes <= 100.",
    ),
    5790637: (
        "edit_war",
        "Mutual revert replacing a 3.6 KB historical society article with a 36-byte redirect on the sister page; falsely excluded by restored_bytes <= 100.",
    ),
    10074761: (
        "edit_war",
        "Mutual revert replacing a 7.2 KB regional history article with a 35-byte redirect; genuine geopolitical redirect war falsely excluded by restored_bytes <= 100.",
    ),
    11800796: (
        "edit_war",
        "Patroller rolled back an 18-edit sequence containing mutual reverts over terminology and administrative boundaries; falsely excluded by wide_span (span=18).",
    ),
    15833686: (
        "content_dispute",
        "Rollback of 19 consecutive anonymous edits that removed a sourced procurement citation and added disparaging financial claims; falsely excluded by wide_span (span=19).",
    ),
    19113146: (
        "edit_war",
        "Mutual revert replacing a 5.3 KB fortress article with a 22-byte island redirect; genuine redirect war falsely excluded by restored_bytes <= 100.",
    ),
    21361599: (
        "bot_maintenance",
        "Bot undid its own edit 26s later on a 93-byte categorized redirect; genuine bot self-revert falsely excluded by restored_bytes <= 100.",
    ),
    23188549: (
        "content_dispute",
        "Editor reverted 35 consecutive edits (+5.3 KB) by one registered account per a community RfC on source distortion; falsely excluded by wide_span (span=35).",
    ),
    23837560: (
        "good_faith_fix",
        "FlaggedRevs rejection of 29 consecutive sourced timeline edits by one temporary account requesting prose instead of a bulleted list; falsely excluded by wide_span (span=29).",
    ),
    # --- bot (20) ---
    3854366: (
        "bot_maintenance",
        "Human editor undid an AWB bot edit that stripped an image alignment parameter while changing an en-dash.",
    ),
    3854387: (
        "bot_maintenance",
        "Human editor undid an AWB bot edit that stripped an image alignment parameter on a stub biography.",
    ),
    3854401: (
        "bot_maintenance",
        "Human editor undid an AWB bot edit that stripped an image alignment parameter on a stub biography.",
    ),
    3854599: (
        "bot_maintenance",
        "Human editor undid an AWB bot edit that stripped an image alignment parameter on a stub biography.",
    ),
    8474089: (
        "bot_maintenance",
        "Interwiki bot reverted another interwiki bot's German language link target.",
    ),
    8743361: (
        "bot_maintenance",
        "Interwiki bot reverted another interwiki bot's English language link target.",
    ),
    8981826: (
        "bot_maintenance",
        "Interwiki bot reverted another interwiki bot's Persian language link spelling.",
    ),
    9837738: (
        "bot_maintenance",
        "Interwiki bot re-added a Spanish interwiki link removed by another bot two weeks earlier.",
    ),
    11241978: (
        "bot_maintenance",
        "Interwiki bot removed a Swedish section-anchor interwiki link; span=4 also sweeps an already-reverted human category edit from 2012.",
    ),
    11853403: (
        "bot_maintenance",
        "Interwiki bot re-added an erroneous German interwiki link (about the subject's daughter) that a human editor had removed 35 days earlier.",
    ),
    12789834: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid Swedish interwiki link added 3.3 months earlier after copying it to Wikidata, recreating the pre-interwiki SHA-1.",
    ),
    12840731: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid Russian interwiki link added 4.5 months earlier after copying it to Wikidata, recreating the pre-interwiki SHA-1.",
    ),
    12868119: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid Russian interwiki link added 13 months earlier after copying it to Wikidata, recreating the 2011 SHA-1.",
    ),
    12912956: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid Swedish interwiki link added 3.5 months earlier after copying it to Wikidata, recreating the 2012 SHA-1.",
    ),
    12995103: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid Russian interwiki link added 10.5 months earlier after copying it to Wikidata, recreating the 2011 SHA-1.",
    ),
    13034187: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid Russian interwiki link added 3.4 years earlier after copying it to Wikidata, recreating the 2009 SHA-1.",
    ),
    13058154: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid French interwiki link added 2.8 months earlier after copying it to Wikidata, recreating the 2012 SHA-1.",
    ),
    13180530: (
        "collision_artifact",
        "2013 Wikidata migration bot removed a valid Estonian interwiki link added 2.0 years earlier after copying it to Wikidata, recreating the 2011 SHA-1.",
    ),
    13191687: (
        "collision_artifact",
        "2013 Wikidata migration bot removed the last migrated Galician interwiki link, matching the 2010 pre-interwiki SHA-1 across 5 valid interwiki edits.",
    ),
    20775516: (
        "bot_maintenance",
        "Human editor undid an archive bot's false-positive dead-link tag because the external link still worked.",
    ),
    # --- self_revert (20) ---
    537596: (
        "self_correction",
        "Registered editor added a university category and removed their own addition 14 minutes later.",
    ),
    1075757: (
        "self_correction",
        "Registered editor accidentally deleted the space in an external link and restored it 30 seconds later.",
    ),
    2791728: (
        "self_correction",
        "Anonymous editor deleted a period and restored it 24 seconds later.",
    ),
    4285265: (
        "self_correction",
        "Anonymous editor tentatively entered an uncertain founding year in the infobox and reverted their own change 23 seconds later.",
    ),
    4956372: (
        "self_correction",
        "Registered editor accidentally stripped a title-coordinates template and undid their own edit 19 minutes later.",
    ),
    5333912: (
        "self_correction",
        "Registered editor broke wikilink brackets inside a template and undid their own edit 14 seconds later.",
    ),
    7785107: (
        "self_correction",
        "Anonymous editor altered one letter in march lyrics and restored the original word 28 seconds later.",
    ),
    8323999: (
        "self_correction",
        "Registered editor added a short book footnote and removed their own addition 104 seconds later.",
    ),
    10092877: (
        "self_correction",
        "Registered editor swapped two synonymous Finnish adverbs and reverted their own wording change 24 seconds later.",
    ),
    10370558: (
        "self_correction",
        "Registered editor rolled back their own mistaken undo from 5 hours earlier, restoring an anonymous editor's valid category.",
    ),
    10419032: (
        "self_correction",
        "Registered editor added an island category via HotCat and undid their own edit 77 seconds later.",
    ),
    10760044: (
        "self_correction",
        "Registered editor added a sentence on EFTA membership and removed it 32 seconds later while reorganizing sections.",
    ),
    13533439: (
        "self_correction",
        "Registered editor added a parent restaurant category via HotCat and removed it 43 seconds later after already adding a narrower subcategory.",
    ),
    15541160: (
        "self_correction",
        "Anonymous editor changed a regional road number from 183 to 186 and undid their own edit 47 seconds later.",
    ),
    16546445: (
        "self_correction",
        "Registered editor added group chairpersons and undid their own edit 41 seconds later upon seeing the fact was already present.",
    ),
    17333311: (
        "self_correction",
        "Article creator changed a Bronze Age category and reverted their own change 45 seconds later.",
    ),
    17659343: (
        "self_correction",
        "Registered editor tested category changes across 4 HotCat edits over 145 seconds before restoring their own starting version.",
    ),
    17770391: (
        "self_correction",
        "Registered editor added two paragraphs on electoral college procedure and reverted their own additions 8 minutes later.",
    ),
    18352152: (
        "self_correction",
        "Anonymous editor accidentally deleted a trailing vowel and restored it 34 seconds later.",
    ),
    22170612: (
        "self_correction",
        "New registered account inserted a two-word insult into a politician's biography and removed their own insertion 4 minutes later.",
    ),
    # --- edit_war (20) ---
    1514877: (
        "edit_war",
        "Mutual revert cycle among editors over placing a language-board spelling recommendation in the lead sentence.",
    ),
    2136446: (
        "edit_war",
        "Mutual revert cycle over inserting a 3.7 KB argumentative essay on 12th-century Baltic politics into the article.",
    ),
    2222503: (
        "edit_war",
        "Repeated revert and re-insertion cycle over replacing the parent election category with a non-existent year-specific category.",
    ),
    4099151: (
        "edit_war",
        "Anonymous editor counter-reverted a patroller to restore a doubled casualty figure (1800 vs 950) in the Lapland War infobox.",
    ),
    4945596: (
        "edit_war",
        "Counter-revert restoring contested non-standard place-name inflection after another editor undid it.",
    ),
    7447858: (
        "edit_war",
        "Mutual revert cycle between two registered editors over a politician's tax-knowledge controversy section citing NPOV policy.",
    ),
    12284021: (
        "good_faith_fix",
        "Patroller reverted vandalism and 102s later fixed a 2-letter verb ending that matched a 10-month-old SHA-1; same-reverter revert in span falsely triggered edit_war.",
    ),
    13322346: (
        "graffiti",
        "Patroller rolled back an anonymous editor repeatedly inserting a notorious criminal's nickname into the President's biography; repeated pure graffiti, not an edit war.",
    ),
    13392281: (
        "good_faith_fix",
        "Single-edit anonymous reader fixed a 1-letter surname spelling discrepancy matching a SHA-1 reverted 3 months earlier; independent good-faith fix, not an edit war.",
    ),
    14650764: (
        "good_faith_fix",
        "Patroller undid the second and then the first of two consecutive unsourced historical edits 2 minutes apart; same-reverter undo in span falsely triggered edit_war.",
    ),
    14661204: (
        "graffiti",
        "Anonymous editor re-inserted a vulgar nickname into a politician's lead with mocking laughter in the summary; repeat vandalism spam, not an editorial edit war.",
    ),
    14818461: (
        "good_faith_fix",
        "Anonymous reader accidentally saved an old December revision (fixing it 96s later with summary 'oops'); unrelated revert 4 months earlier in span falsely triggered edit_war.",
    ),
    15661125: (
        "edit_war",
        "Mutual revert war over unsourced claims of partisan affiliation and Freemasonry among newspaper editors-in-chief.",
    ),
    18130289: (
        "edit_war",
        "Block-evading anonymous editor counter-reverted a patroller across a 4.4 KB terminology rewrite on Finnish territorial changes.",
    ),
    18923569: (
        "graffiti",
        "Anonymous editor forged a block-evasion patrol summary to re-insert imageboard meme graffiti into the COVID-19 article; repeat vandalism, not an edit war.",
    ),
    18952185: (
        "edit_war",
        "Patroller rolled back a block-evading editor's counter-revert over listing Prime Ministers inside Regent Svinhufvud's succession box.",
    ),
    19496784: (
        "edit_war",
        "Patroller reverted a block-evading editor's counter-revert renaming Karelian town Uhtua to Kalevala throughout the article.",
    ),
    19508285: (
        "edit_war",
        "Patroller reverted a block-evading editor's counter-revert forcing a disputed Finnish-name cleanup banner onto a Russian town article.",
    ),
    19878106: (
        "graffiti",
        "Patroller rolled back an anonymous editor who re-inserted filter-evading accented profanity as a cabinet minister's occupation 24s after rollback; pure graffiti.",
    ),
    22122931: (
        "edit_war",
        "Mutual revert war over replacing a sports federation PNG logo with an SVG uploaded by a globally locked account.",
    ),
    # --- dispute (20) ---
    1037426: (
        "good_faith_fix",
        "Registered editor added a second municipality template explicitly inviting removal if redundant, and another editor removed it; cooperative housekeeping, not a dispute.",
    ),
    4404930: (
        "good_faith_fix",
        "Patroller rolled back two misplaced commas (+2 bytes, zero prose change) by a registered editor; trivial punctuation cleanup, not a content dispute.",
    ),
    8861463: (
        "content_dispute",
        "Editorial disagreement over infobox framing of whether historical Lapland was a province of modern Finland or areas within modern borders.",
    ),
    10144934: (
        "content_dispute",
        "Substantive dispute over removing a claim citing a controversial 2010 book on alleged Soviet backing for Mauno Koivisto.",
    ),
    10267506: (
        "content_dispute",
        "Editorial disagreement over whether 1965 successor-party merger history belongs in the National Progressive Party article.",
    ),
    10575248: (
        "content_dispute",
        "Dispute over removing a poll-based claim about Finns Party voters' second-choice presidential preferences, later counter-reverted with argument.",
    ),
    13485637: (
        "graffiti",
        "FlaggedRevs rejection of juvenile slang graffiti replacing section headings on Continuation War; landed in dispute only because one author was a 1-edit registered account.",
    ),
    14301739: (
        "content_dispute",
        "Disagreement over whether a 1994 archival broadcast is valid sourcing for a former president's current residence.",
    ),
    14911851: (
        "good_faith_fix",
        "Routine removal of a promotional travel-blog link added by an anonymous editor; landed in dispute only because the IP briefly re-inserted and self-reverted 80s later.",
    ),
    14914118: (
        "good_faith_fix",
        "Registered editor used undo to remove an admin's page-protection template two days after the 7-day protection expired; routine administrative cleanup, not a dispute.",
    ),
    16014464: (
        "good_faith_fix",
        "Editor moved a person-level category ('Mass murderers') from the school-shooting event article to the perpetrator redirect per categorization convention.",
    ),
    16774997: (
        "graffiti",
        "Rollback 36s later of a racial slur inserted into Mannerheim's biography by a 2-edit registered account; obvious graffiti misclassified as dispute.",
    ),
    16943757: (
        "graffiti",
        "FlaggedRevs rejection of toilet-humor link replacement ('Pieru') by a 3-edit registered account; obvious graffiti misclassified as dispute.",
    ),
    18971298: (
        "good_faith_fix",
        "FlaggedRevs rejection of an unsourced box-office attendance number added in good faith by a registered editor ('Source missing').",
    ),
    19773746: (
        "good_faith_fix",
        "FlaggedRevs removal of an unsourced non-notable local restaurant owner added by a 1-edit newcomer under notable residents.",
    ),
    20565120: (
        "graffiti",
        "FlaggedRevs rejection 6m later of absurd leetspeak nonsense ('H1tler... 2 fighter jets') inserted by a 2-edit registered account; obvious graffiti misclassified as dispute.",
    ),
    20773657: (
        "content_dispute",
        "Substantive BLP/weight dispute over adding a 1.2 KB tabloid controversy subsection to PM Sanna Marin's biography ('Wikipedia is not a gossip column').",
    ),
    22965653: (
        "good_faith_fix",
        "Anonymous editor removed an empty Footnotes heading and template 27s after a registered editor added them without inline ref tags; minor markup fix.",
    ),
    23049644: (
        "good_faith_fix",
        "FlaggedRevs correction of a 1-edit newcomer adding an 1872 Diet member to the wrong municipality's notable-people list.",
    ),
    23611133: (
        "good_faith_fix",
        "Collegial image-style undo preferring an unannotated sculpture photo over an annotated file; zero prose change and no conflict.",
    ),
    # --- graffiti_cleanup (20) ---
    471819: (
        "good_faith_fix",
        "Anonymous editor replaced a convicted murderer's full name with initial 'M' across two edits (court-privacy style); misguided anonymization, not graffiti.",
    ),
    786348: (
        "graffiti",
        "Rollback 105s later of explicit sexual profanity inserted into a sitting president's biography.",
    ),
    1673141: (
        "graffiti",
        "Rollback 44s later of elongated profanity ('haaaaista paaaska...') inserted into a historical movement article.",
    ),
    2697530: (
        "good_faith_fix",
        "Undo 13.8h later of an anonymous section edit removing a single 'See also' etymology link without profanity or nonsense.",
    ),
    3990562: (
        "graffiti",
        "Rollback 19s later of a defamatory personal-life hoax inserted into a sitting president's biography.",
    ),
    6991286: (
        "graffiti",
        "Rollback 61s later of a personal family comment with smiley ('he was my great-great-granddad! :)') typed into the Sources section.",
    ),
    8804103: (
        "graffiti",
        "Rollback 93s later of a vulgar insult calling the Finance Minister an obscenity.",
    ),
    11146993: (
        "graffiti",
        "FlaggedRevs rejection 35s later of juvenile infobox defacement ('WC', 'soinisaurus rex', vulgarity).",
    ),
    11240649: (
        "good_faith_fix",
        "Undo 54s later of an active anonymous editor mistakenly adding a political-satire TV character category to a real prime minister's biography.",
    ),
    12517754: (
        "edit_war",
        "Fourth re-insertion by an anonymous editor of true same-birthday actress trivia after multiple named editors removed it; slight rewording evaded SHA-1 edit_war detection.",
    ),
    13177173: (
        "graffiti",
        "Rollback 129s later of an anonymous edit replacing the bolded title 'Kalevala' with first name 'Miska'.",
    ),
    14923490: (
        "graffiti",
        "Removal 3.2h later of a joke notable-person entry ('game engine professional') added by an anonymous editor.",
    ),
    15721243: (
        "graffiti",
        "Rollback 90s later of gibberish ('Kukkelis kokkelis') replacing Urho Kekkonen's given names.",
    ),
    17317747: (
        "graffiti",
        "Rollback 42s later of vulgar shock vandalism and abusive edit summary on the Jäger movement article.",
    ),
    17886580: (
        "graffiti",
        "Rollback 158s later of slang graffiti ('Jonne on / randomi') replacing a cabinet minister's given names.",
    ),
    18872675: (
        "graffiti",
        "Rollback 47s later of hit-and-run name-calling ('and traitor to the country') stuffed into an infobox occupation field.",
    ),
    19074906: (
        "good_faith_fix",
        "FlaggedRevs rejection 4h later of a valid Finnish hydrographic synonym ('eli luusuassa' = at the lake outlet) as redundant ('tarpeeton lisäys'), not vandalism.",
    ),
    19352617: (
        "graffiti",
        "Rollback 32s later of an insult ('and village idiot') appended to a living person's lead sentence.",
    ),
    19536068: (
        "graffiti",
        "FlaggedRevs rejection 23m later of a joke notable-person entry ('Sausage Markus, well-known sausage addict').",
    ),
    22157920: (
        "graffiti",
        "FlaggedRevs rejection 146s later of a youth ice-hockey team name-drop ('Konsta rautio kärpät u15 musta') on a former municipality page.",
    ),
    # --- other (20) ---
    4002853: (
        "graffiti",
        "Rollback 40.5h later of an unexplained false hometown change in a band infobox (Helsinki -> Lappajärvi); slow-caught vandalism missed by the 24h cutoff.",
    ),
    6757195: (
        "graffiti",
        "Revert 4 days later of homophobic slurs replacing band members in the infobox; obvious graffiti missed solely because survival exceeded 24h.",
    ),
    7200594: (
        "graffiti",
        "Undo 6 months later of single-word juvenile graffiti ('kakka') on a low-traffic band stub; missed by the 24h cutoff.",
    ),
    8385193: (
        "graffiti",
        "Removal 6 months later of a pun/hoax high-school principal name ('Lucina Väärälä'); slow-caught graffiti missed by the 24h cutoff.",
    ),
    10274975: (
        "good_faith_fix",
        "Rollback 45h later of a clumsy good-faith attempt to note a murder suspect's 1998 death year inside an existing book citation tag.",
    ),
    11480728: (
        "graffiti",
        "Rollback 36h later of a garbled false linguistic claim ('only Finnish word with two consecutive H letters'); slow-caught hoax/graffiti.",
    ),
    14544474: (
        "content_dispute",
        "Popups revert 2.7 years later of a 1.8 KB BLP removal by an anonymous editor claiming to be the subject removing outdated unsourced claims; real BLP dispute.",
    ),
    16151643: (
        "content_dispute",
        "Revert 28.6h later ('per talk') of a prolific IP changing a 1918 Senate department head's title to '1st Minister for Foreign Affairs'; contested historical framing.",
    ),
    16151729: (
        "content_dispute",
        "Revert 26.5h later ('per Village Pump discussion') of the same prolific IP's campaign adding ordinal numbering to cabinet minister infoboxes.",
    ),
    18285510: (
        "good_faith_fix",
        "Undo 4.3 days later of a poorly formatted unsourced floorball player entry under notable residents.",
    ),
    18938588: (
        "good_faith_fix",
        "Rollback 22 days later of an anonymous editor replacing the general ambassadors category with three non-existent country-specific categories.",
    ),
    18939639: (
        "good_faith_fix",
        "HotCat revert 22 days later of the same non-existent country-specific ambassador category per category talk consensus.",
    ),
    20680315: (
        "good_faith_fix",
        "Undo 3.8 days later of an anonymous edit appending a 2021 spin-off host subtitle ('by Roni Bäck') to the main TV show's title and infobox.",
    ),
    20822320: (
        "good_faith_fix",
        "FlaggedRevs rejection 18 days later of an unsourced qualifier ('erroneously') inserted into a cited sentence ('Does this match the cited source?').",
    ),
    21623001: (
        "good_faith_fix",
        "Undo 30h later of an unsourced death date added with a polite summary asking experienced editors to check the formatting.",
    ),
    21738294: (
        "good_faith_fix",
        "FlaggedRevs rejection 16 days later of an unsourced footballer name under notable residents ('restore with a source').",
    ),
    22149561: (
        "good_faith_fix",
        "FlaggedRevs rejection 49 days later of a well-meaning compound-word hypercorrection ('perheen isä' -> 'perheenisä', 'Was not an error').",
    ),
    22157037: (
        "content_dispute",
        "FlaggedRevs rejection 41h later (after bot stabilization) of 2 anonymous edits deleting sourced criticism sections on an MP with NPOV arguments in summaries.",
    ),
    22186629: (
        "good_faith_fix",
        "FlaggedRevs rejection 41h later of an unsourced 3x increase to Red Guard troop numbers in a 1918 Civil War battle infobox.",
    ),
    22520662: (
        "good_faith_fix",
        "Undo 7 days later of an unsourced sentence naming the training ship Suomen Joutsen's first mate.",
    ),
}


def load_sampled_rows() -> list[dict]:
    """Reproduce the exact 7 x 20 sampled rows from research.s1_revert and verify against results/sample_<class>.md."""
    sampled_all: list[dict] = []
    for cls in S1_CLASSES:
        rows = aot.sql(
            f"""
            SELECT r.page_id,
                   p.title,
                   r.reverting_rev_id,
                   r.restored_rev_id,
                   r.first_reverted_rev_id,
                   r.last_reverted_rev_id,
                   r.reverted_count,
                   r.reverted_user_count,
                   r.reverted_users_anonymous_only,
                   r.reverting_user,
                   r.reverting_anonymous,
                   r.revert_ts,
                   r.first_reverted_ts,
                   r.seconds_to_revert,
                   r.net_bytes,
                   r.r_bytes,
                   r.comment_family,
                   r.exclude_reason,
                   r.reinserted_rev_id,
                   r.class,
                   r.reverting_comment
            FROM research.s1_revert r
            JOIN research.s1_page_stats p USING (page_id)
            WHERE r.class = '{cls}'
            ORDER BY r.reverting_rev_id ASC
            """
        )
        rng = random.Random(f"s1-28-{cls}")
        picked = rng.sample(rows, min(20, len(rows)))

        sample_md = (S1_DIR / "results" / f"sample_{cls}.md").read_text(encoding="utf-8")
        md_ids = [int(m) for m in re.findall(r"revert \[diff=(\d+)\]", sample_md)]
        picked_ids = [int(r["reverting_rev_id"]) for r in picked]
        if sorted(md_ids) != sorted(picked_ids):
            raise RuntimeError(f"Sample mismatch for {cls}: md={md_ids} vs sql={picked_ids}")
        sampled_all.extend(picked)
    return sampled_all


def fetch_texts_and_diffs(sampled_all: list[dict]) -> tuple[dict[int, str | None], dict[int, dict]]:
    """Fetch <= 300 revision texts via aot.rev_texts and diff restored vs last_reverted via wikidiff.sh batch."""
    pairs = []
    for r in sampled_all:
        pid = int(r["page_id"])
        pairs.append((pid, int(r["restored_rev_id"])))
        pairs.append((pid, int(r["last_reverted_rev_id"])))
    unique_pairs = list(dict.fromkeys(pairs))
    if len(unique_pairs) > 300:
        raise RuntimeError(f"Text fetch budget exceeded: {len(unique_pairs)} > 300")
    aot.log(f"[review] fetching {len(unique_pairs)} unique revision texts via aot.rev_texts (limit 300)")
    texts = aot.rev_texts(unique_pairs)

    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    diffs_cache_path = CACHE_DIR / "diffs.json"
    expected_ids = {int(r["reverting_rev_id"]) for r in sampled_all}
    if diffs_cache_path.exists():
        cached = {int(k): v for k, v in json.loads(diffs_cache_path.read_text(encoding="utf-8")).items()}
        if set(cached.keys()) == expected_ids:
            aot.log(f"[review] loaded {len(cached)} cached diffs from {diffs_cache_path}")
            return texts, cached

    batch_lines = []
    for r in sampled_all:
        rid = int(r["reverting_rev_id"])
        old_t = texts.get(int(r["restored_rev_id"]))
        new_t = texts.get(int(r["last_reverted_rev_id"]))
        batch_lines.append(json.dumps({"id": str(rid), "old": old_t, "new": new_t}, ensure_ascii=False))

    aot.log(f"[review] running scripts/wikidiff.sh batch on {len(batch_lines)} pairs")
    proc = subprocess.run(
        [str(REPO / "scripts/wikidiff.sh"), "batch"],
        input=chr(10).join(batch_lines) + chr(10),
        capture_output=True,
        text=True,
        cwd=str(REPO),
        check=True,
    )
    diffs_by_id: dict[int, dict] = {}
    for line in proc.stdout.splitlines():
        if line.strip():
            d = json.loads(line)
            diffs_by_id[int(d["id"])] = d
    diffs_cache_path.write_text(
        json.dumps({str(k): v for k, v in diffs_by_id.items()}, ensure_ascii=False, indent=2) + chr(10),
        encoding="utf-8",
    )
    aot.log(f"[review] saved {len(diffs_by_id)} diffs to {diffs_cache_path}")
    return texts, diffs_by_id


def inspect_class(cls: str, sampled_all: list[dict], texts: dict[int, str | None], diffs_by_id: dict[int, dict]) -> None:
    cls_rows = [r for r in sampled_all if r["class"] == cls]
    page_ids_csv = ",".join(str(int(r["page_id"])) for r in cls_rows)

    all_page_revs = aot.sql(
        f"""
        SELECT page_id, rev_id, parent_id, ts, user, anonymous, bytes, sha1, comment
        FROM corpus.revision
        WHERE page_id IN ({page_ids_csv})
        ORDER BY page_id ASC, ts ASC, rev_id ASC
        """
    )
    revs_by_page: dict[int, list[dict]] = {}
    for pr in all_page_revs:
        revs_by_page.setdefault(int(pr["page_id"]), []).append(pr)

    rids_csv = ",".join(str(int(r["reverting_rev_id"])) for r in cls_rows)
    span_owned = aot.sql(
        f"""
        SELECT reverting_rev_id, rev_id, user, anonymous, ts, bytes, comment
        FROM research.s1_reverted_edit
        WHERE reverting_rev_id IN ({rids_csv})
        ORDER BY reverting_rev_id ASC, ts ASC, rev_id ASC
        """
    )
    owned_by_revert: dict[int, list[dict]] = {}
    for sr in span_owned:
        owned_by_revert.setdefault(int(sr["reverting_rev_id"]), []).append(sr)

    all_s1_revs = aot.sql(
        f"""
        SELECT reverting_rev_id, restored_rev_id, class, exclude_reason
        FROM research.s1_revert
        WHERE page_id IN ({page_ids_csv})
        """
    )
    s1_by_rev = {int(x["reverting_rev_id"]): x for x in all_s1_revs}

    user_stats = {
        u["user"]: u
        for u in aot.sql(
            f"""
            SELECT us.user, us.edits, us.non_null_edits, us.reverted_edits_by_others, us.reverts_made_others, us.is_bot
            FROM research.s1_user_stats us
            JOIN (
                SELECT DISTINCT user FROM corpus.revision
                WHERE page_id IN ({page_ids_csv}) AND user IS NOT NULL
            ) pu USING (user)
            """
        )
    }

    for idx, r in enumerate(cls_rows, 1):
        rid = int(r["reverting_rev_id"])
        pid = int(r["page_id"])
        rest_id = int(r["restored_rev_id"])
        last_id = int(r["last_reverted_rev_id"])
        prevs = revs_by_page[pid]
        pos_by_id = {int(pr["rev_id"]): i for i, pr in enumerate(prevs)}
        p_rest = pos_by_id.get(rest_id, 0)
        p_rev = pos_by_id.get(rid, 0)

        print("=" * 100)
        print(
            f"[{cls} #{idx}] rev={rid} | page={pid} ({r['title']}) | "
            f"restores={rest_id}, last_reverted={last_id} | "
            f"span={r['reverted_count']} (owned={len(owned_by_revert.get(rid, []))}) | "
            f"sec={r['seconds_to_revert']}s | r_bytes={r['r_bytes']} (net={r['net_bytes']}) | "
            f"fam={r['comment_family']} | excl={r['exclude_reason']} | reins={r['reinserted_rev_id']}"
        )
        ru = r["reverting_user"]
        rus = user_stats.get(ru, {})
        print(
            f"  Reverter: {aot.user_display(ru)} (anon={r['reverting_anonymous']}, bot={rus.get('is_bot')}, "
            f"edits={rus.get('edits')}, revd_by_others={rus.get('reverted_edits_by_others')}, "
            f"reverts_others={rus.get('reverts_made_others')})"
        )
        print(f"  Revert comment: {aot.mask_comment(r['reverting_comment'])}")

        lo = max(0, p_rest - 3)
        hi = min(len(prevs), p_rev + 5)
        window = prevs[lo:hi]
        print("  --- History window ---")
        if len(window) > 18:
            shown = window[:8] + [None] + window[-8:]
        else:
            shown = window
        for pr in shown:
            if pr is None:
                print(f"    ... ({len(window) - 16} intermediate revisions omitted) ...")
                continue
            pr_id = int(pr["rev_id"])
            marker = "   "
            if pr_id == rest_id:
                marker = "[P]"
            elif pr_id == rid:
                marker = "[R]"
            elif p_rest < pos_by_id[pr_id] < p_rev:
                marker = "[S]"
            s1_tag = ""
            if pr_id in s1_by_rev:
                s1_tag = f" <s1:{s1_by_rev[pr_id]['class']}->{s1_by_rev[pr_id]['restored_rev_id']}>"
            u = pr["user"]
            us = user_stats.get(u, {})
            u_info = f"{aot.user_display(u)}"
            if us and pr_id != rid and marker == "[S]":
                u_info += f"(e={us.get('edits')},rb={us.get('reverted_edits_by_others')})"
            print(
                f"    {marker} rev={pr_id} ts={pr['ts']} bytes={pr['bytes']} sha1={str(pr['sha1'])[:8]} "
                f"user={u_info}{s1_tag} | {aot.mask_comment(pr['comment'])[:140]}"
            )

        d = diffs_by_id[rid]
        old_raw = texts.get(rest_id)
        new_raw = texts.get(last_id)
        old_len = len(old_raw) if old_raw is not None else -1
        new_len = len(new_raw) if new_raw is not None else -1
        print(f"  --- WikiDiff (old=restored {rest_id} [{old_len}c] -> new=last_reverted {last_id} [{new_len}c]) ---")
        if "error" in d:
            print(f"    ERROR: {d['error']}")
        else:
            print(f"    stats: {json.dumps(d['stats'])}")
            if old_raw is not None and len(old_raw) <= 300:
                print(f"    restored raw (<=300c): {aot.mask_comment(old_raw)!r}")
            if new_raw is not None and len(new_raw) <= 300:
                print(f"    last_rev raw (<=300c): {aot.mask_comment(new_raw)!r}")
            if d.get("added_prose"):
                print(f"    added_prose:   {aot.mask_comment(d['added_prose'])[:1500]!r}")
            if d.get("removed_prose"):
                print(f"    removed_prose: {aot.mask_comment(d['removed_prose'])[:1500]!r}")
            for seg in d.get("segments", [])[:40]:
                if seg.get("moved"):
                    print(f"    [MOVED {seg['kind']}] {aot.mask_comment(seg['text'])[:240]!r}")
                else:
                    print(
                        f"    [{seg['op'].upper()} {seg['kind']}] text={aot.mask_comment(seg['text'])[:300]!r} "
                        f"| prose={aot.mask_comment(seg['prose'])[:240]!r}"
                    )
            if len(d.get("segments", [])) > 40:
                print(f"    ... ({len(d['segments']) - 40} more segments) ...")
        print()


def build_and_write_verdicts(sampled_all: list[dict]) -> list[dict]:
    """Validate all 140 recorded verdicts and write verdicts.jsonl."""
    sampled_ids = [int(r["reverting_rev_id"]) for r in sampled_all]
    missing = [rid for rid in sampled_ids if rid not in VERDICTS]
    extra = [rid for rid in VERDICTS if rid not in set(sampled_ids)]
    if missing or extra:
        raise RuntimeError(f"Verdict key mismatch: missing={missing}, extra={extra}")

    banned_re = re.compile(r"\b(troll\w*|propagandist\w*|proven)\b", re.IGNORECASE)
    wikitext_re = re.compile(r"(\[\[|\]\]|\{\{|\}\}|<ref\b)")

    records: list[dict] = []
    for r in sampled_all:
        rid = int(r["reverting_rev_id"])
        s1_cls = str(r["class"])
        verdict, note = VERDICTS[rid]
        if verdict not in VALID_VERDICTS:
            raise ValueError(f"Invalid verdict {verdict!r} for rev {rid}")
        if not note or not note.strip():
            raise ValueError(f"Empty note for rev {rid}")
        if aot.mask_comment(note) != note:
            raise ValueError(f"Unmasked IP/temp token in note for rev {rid}: {note!r}")
        if banned_re.search(note):
            raise ValueError(f"Non-neutral wording in note for rev {rid}: {note!r}")
        if wikitext_re.search(note):
            raise ValueError(f"Wikitext markup in note for rev {rid}: {note!r}")

        agrees = verdict == EXPECTED_VERDICT_BY_CLASS[s1_cls]
        records.append(
            {
                "reverting_rev_id": rid,
                "s1_class": s1_cls,
                "verdict": verdict,
                "agrees": agrees,
                "note": note,
            }
        )

    out_path = HERE / "verdicts.jsonl"
    out_path.write_text(
        "".join(json.dumps(rec, ensure_ascii=False) + chr(10) for rec in records),
        encoding="utf-8",
    )
    aot.log(f"[review] wrote {len(records)} verdicts to {out_path}")
    return records


def print_summary(records: list[dict]) -> None:
    """Print per-class agreement rates and the 7 x 8 confusion table to stdout."""
    print("## Per-class agreement" + chr(10))
    print("| S1 class | Expected verdict | Agrees | n | Rate |")
    print("|---|---|---:|---:|---:|")
    total_agrees = 0
    for cls in S1_CLASSES:
        cls_recs = [r for r in records if r["s1_class"] == cls]
        ag = sum(1 for r in cls_recs if r["agrees"])
        total_agrees += ag
        print(f"| `{cls}` | `{EXPECTED_VERDICT_BY_CLASS[cls]}` | {ag} | {len(cls_recs)} | {100.0 * ag / len(cls_recs):.1f}% |")
    print(f"| **Total** | — | **{total_agrees}** | **{len(records)}** | **{100.0 * total_agrees / len(records):.1f}%** |" + chr(10))

    print("## Confusion table (S1 class × independent verdict)" + chr(10))
    header = r"| S1 class \ Verdict | " + " | ".join(f"`{v}`" for v in VALID_VERDICTS) + " | Total |"
    sep = "|---|" + "|".join("---:" for _ in VALID_VERDICTS) + "|---:|"
    print(header)
    print(sep)
    col_totals = {v: 0 for v in VALID_VERDICTS}
    for cls in S1_CLASSES:
        cls_recs = [r for r in records if r["s1_class"] == cls]
        counts = {v: 0 for v in VALID_VERDICTS}
        for r in cls_recs:
            counts[r["verdict"]] += 1
            col_totals[r["verdict"]] += 1
        cells = []
        for v in VALID_VERDICTS:
            c = counts[v]
            if v == EXPECTED_VERDICT_BY_CLASS[cls]:
                cells.append(f"**{c}**")
            else:
                cells.append(str(c))
        print(f"| `{cls}` | " + " | ".join(cells) + f" | {len(cls_recs)} |")
    print("| **Total** | " + " | ".join(f"**{col_totals[v]}**" for v in VALID_VERDICTS) + f" | **{len(records)}** |")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--inspect", choices=S1_CLASSES, help="Inspect diffs for one S1 class")
    args = parser.parse_args()

    sampled_all = load_sampled_rows()
    texts, diffs_by_id = fetch_texts_and_diffs(sampled_all)

    if args.inspect:
        inspect_class(args.inspect, sampled_all, texts, diffs_by_id)
        return

    records = build_and_write_verdicts(sampled_all)
    print_summary(records)


if __name__ == "__main__":
    main()
