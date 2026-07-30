import Phaser from "phaser";
import type { GameData, TeamRecord } from "../data/types";
import { triggerLabel } from "../game/abilities";
import { faceFrame } from "../game/dice";
import { BALL_ZONES, Match, teamFromLineup, type Encounter, type Side } from "../game/match";
import {
  ATLAS_BALL,
  ATLAS_DICE,
  ATLAS_FIELD,
  ATLAS_PROMPT,
  IMAGE_PLAY_ON,
  IMAGE_PLAY_ON_PRESSED,
  IMAGE_SCOREBOARD,
} from "./BootScene";

const FONT = "Krungthep";
const DICE_SIZE = 130;
const COLUMN_X: Record<Side, number> = { home: 170, away: 470 };
const ROW_Y = [330, 500, 670];
/** Team totals sit in the corners of the pitch, as in the original. */
const TOTAL_X: Record<Side, number> = { home: 50, away: 590 };
const TOTAL_Y = 700;
const BALL_LANE_Y = 235;

/** The training season lineups the original game starts you with. */
const OPENING_SEASON = "Season0";
const OPENING_MODE = "League";

export class MatchScene extends Phaser.Scene {
  private gameData!: GameData;
  private match!: Match;
  private dice: Record<Side, Phaser.GameObjects.Image[]> = { home: [], away: [] };
  private diceLabels: Record<Side, Phaser.GameObjects.Text[]> = { home: [], away: [] };
  private totals!: Record<Side, Phaser.GameObjects.Text>;
  private scoreText!: Phaser.GameObjects.Text;
  private clockText!: Phaser.GameObjects.Text;
  private logText!: Phaser.GameObjects.Text;
  private ball!: Phaser.GameObjects.Image;
  private banner!: Phaser.GameObjects.Image;
  private playOn!: Phaser.GameObjects.Image;

  constructor() {
    super("match");
  }

  init(payload: { data: GameData }): void {
    this.gameData = payload.data;
  }

  create(): void {
    const teams = this.gameData.seasons.get(OPENING_SEASON)?.get(OPENING_MODE) ?? [];
    const opponent = teams[0];
    if (opponent === undefined) throw new Error("no opponent found in the extracted match lineups");

    this.match = new Match(
      teamFromLineup(this.gameData, "Your Team", this.gameData.initialRoster),
      teamFromLineup(this.gameData, opponent.name, opponent.lineup),
      this.gameData.abilities,
      { matchSeconds: 15 * 60 },
    );

    this.drawField();
    this.drawScoreboard(opponent);
    this.drawDice();
    this.drawControls();
    this.refresh();
  }

  private drawField(): void {
    const field = this.add.image(this.scale.width / 2, this.scale.height / 2, ATLAS_FIELD, "Field.png");
    field.setAngle(90);
    field.setScale(Math.max(this.scale.width / field.height, this.scale.height / field.width));

    this.ball = this.add.image(0, BALL_LANE_Y, ATLAS_BALL, "GameSoccerBall.png").setScale(0.8);
    this.banner = this.add.image(this.scale.width / 2, this.scale.height / 2, ATLAS_PROMPT, "Tie.png");
    this.banner.setVisible(false).setDepth(10);
  }

  private drawScoreboard(opponent: TeamRecord): void {
    this.add.image(this.scale.width / 2, 75, IMAGE_SCOREBOARD);
    this.scoreText = this.add
      .text(this.scale.width / 2, 60, "0 - 0", { fontFamily: FONT, fontSize: "52px", color: "#3b2b12" })
      .setOrigin(0.5);
    this.add
      .text(120, 118, "YOUR TEAM", { fontFamily: FONT, fontSize: "20px", color: "#3b2b12" })
      .setOrigin(0.5);
    this.add
      .text(520, 118, opponent.name.toUpperCase(), { fontFamily: FONT, fontSize: "20px", color: "#3b2b12" })
      .setOrigin(0.5);
  }

  private drawDice(): void {
    for (const side of ["home", "away"] as const) {
      for (const row of ROW_Y) {
        this.add.image(COLUMN_X[side], row, ATLAS_DICE, "DiceBackground.png").setDisplaySize(DICE_SIZE, DICE_SIZE);
        this.dice[side].push(this.add.image(COLUMN_X[side], row, ATLAS_DICE, faceFrame(1)));
        this.diceLabels[side].push(
          this.add
            .text(COLUMN_X[side], row + DICE_SIZE / 2 + 14, "", {
              fontFamily: FONT,
              fontSize: "18px",
              color: "#ffffff",
              stroke: "#000000",
              strokeThickness: 4,
            })
            .setOrigin(0.5),
        );
      }
    }

    this.totals = {
      home: this.add
        .text(TOTAL_X.home, TOTAL_Y, "0", { fontFamily: FONT, fontSize: "44px", color: "#2f7d32", stroke: "#ffffff", strokeThickness: 5 })
        .setOrigin(0.5),
      away: this.add
        .text(TOTAL_X.away, TOTAL_Y, "0", { fontFamily: FONT, fontSize: "44px", color: "#c62828", stroke: "#ffffff", strokeThickness: 5 })
        .setOrigin(0.5),
    };
  }

  private drawControls(): void {
    this.playOn = this.add.image(this.scale.width / 2, 880, IMAGE_PLAY_ON).setInteractive({ useHandCursor: true });
    this.playOn.on(Phaser.Input.Events.GAMEOBJECT_POINTER_DOWN, () => this.playOn.setTexture(IMAGE_PLAY_ON_PRESSED));
    this.playOn.on(Phaser.Input.Events.GAMEOBJECT_POINTER_UP, () => {
      this.playOn.setTexture(IMAGE_PLAY_ON);
      this.playEncounter();
    });
    this.input.keyboard?.on("keydown-SPACE", () => this.playEncounter());

    this.clockText = this.add
      .text(this.scale.width / 2, 935, "", { fontFamily: FONT, fontSize: "28px", color: "#ffffff", stroke: "#000000", strokeThickness: 4 })
      .setOrigin(0.5);
    this.logText = this.add
      .text(this.scale.width / 2, 800, "Tap PLAY ON to kick off", {
        fontFamily: FONT,
        fontSize: "20px",
        color: "#ffffff",
        align: "center",
        stroke: "#000000",
        strokeThickness: 4,
        wordWrap: { width: this.scale.width - 60 },
      })
      .setOrigin(0.5);
  }

  private playEncounter(): void {
    if (this.match.snapshot().finished) return;
    const encounter = this.match.play();
    this.showEncounter(encounter);
    this.refresh();
  }

  private showEncounter(encounter: Encounter): void {
    for (const side of ["home", "away"] as const) {
      encounter.dice[side].forEach((die, index) => {
        this.dice[side][index]?.setFrame(faceFrame(die.rolled.face));
        const ability = this.gameData.abilities.get(die.player.abilityId);
        const combo = ability === undefined ? "" : ` ${triggerLabel(ability.trigger)}`;
        this.diceLabels[side][index]?.setText(`${die.player.name}${combo}`);
      });
      this.totals[side].setText(String(encounter.totals[side]));
    }

    const lines: string[] = [];
    for (const entry of encounter.triggered) {
      const owner = entry.side === "home" ? "YOU" : "THEM";
      lines.push(`${owner}: ${entry.ability.name} (${entry.effect.kind})`);
    }
    if (encounter.shot !== null) {
      lines.push(
        encounter.shot.goal
          ? `GOAL! ${encounter.shot.shooter} beats the keeper's ${encounter.shot.keeper}`
          : `SAVED! keeper ${encounter.shot.keeper} stops ${encounter.shot.shooter}`,
      );
    } else if (encounter.winner === null) {
      lines.push("Tie — nothing moves, roll again");
    } else {
      lines.push(encounter.winner === "home" ? "You win the ball" : "They win the ball");
    }
    this.logText.setText(lines.join("\n"));

    this.showBanner(encounter);
  }

  private showBanner(encounter: Encounter): void {
    const frame =
      encounter.shot !== null
        ? encounter.shot.goal
          ? "Goal.png"
          : "Save.png"
        : encounter.winner === null
          ? "Tie.png"
          : encounter.winner === "home"
            ? "PossessionChangeGreen.png"
            : "PossessionChangeRed.png";
    this.banner.setFrame(frame).setVisible(true).setAlpha(1).setScale(1);
    this.tweens.add({
      targets: this.banner,
      alpha: 0,
      scale: 1.3,
      duration: 700,
      onComplete: () => this.banner.setVisible(false),
    });
  }

  private refresh(): void {
    const state = this.match.snapshot();
    this.scoreText.setText(`${state.score.home} - ${state.score.away}`);
    const minutes = Math.floor(state.secondsRemaining / 60);
    const seconds = state.secondsRemaining % 60;
    this.clockText.setText(`${minutes}:${String(seconds).padStart(2, "0")}`);

    const lane = (this.scale.width - 120) / (BALL_ZONES - 1);
    this.tweens.add({ targets: this.ball, x: 60 + state.ballZone * lane, duration: 250 });

    if (state.finished) {
      this.playOn.disableInteractive().setAlpha(0.5);
      const result =
        state.score.home === state.score.away ? "DRAW" : state.score.home > state.score.away ? "YOU WIN!" : "YOU LOSE";
      this.logText.setText(`FULL TIME — ${result}`);
    }
  }
}
