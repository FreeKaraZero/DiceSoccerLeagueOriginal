import Phaser from "phaser";
import { loadGameData } from "../data/load";
import type { GameData } from "../data/types";

export const ATLAS_DICE = "dice";
export const ATLAS_BALL = "playerBody";
export const ATLAS_PROMPT = "prompt";
export const ATLAS_FIELD = "field";
export const IMAGE_SCOREBOARD = "scoreboard";
export const IMAGE_PLAY_ON = "playOn";
export const IMAGE_PLAY_ON_PRESSED = "playOnPressed";

export class BootScene extends Phaser.Scene {
  constructor() {
    super("boot");
  }

  preload(): void {
    const message = this.add
      .text(this.scale.width / 2, this.scale.height / 2, "Loading…", { fontFamily: "Krungthep", fontSize: "32px" })
      .setOrigin(0.5);
    this.load.once(Phaser.Loader.Events.COMPLETE, () => message.destroy());

    this.load.atlas(ATLAS_DICE, "atlas/Buff6.png", "atlas/Buff6.json");
    this.load.atlas(ATLAS_BALL, "atlas/PlayerBody3.png", "atlas/PlayerBody3.json");
    this.load.atlas(ATLAS_PROMPT, "atlas/Prompt1.png", "atlas/Prompt1.json");
    this.load.atlas(ATLAS_FIELD, "atlas/Season0Field2(1.1).png", "atlas/Season0Field2(1.1).json");
    this.load.image(IMAGE_SCOREBOARD, "images/scoreboardBG.png");
    this.load.image(IMAGE_PLAY_ON, "images/PlayOnButton.png");
    this.load.image(IMAGE_PLAY_ON_PRESSED, "images/PlayOnButtonPressed.png");
  }

  create(): void {
    void this.start();
  }

  private async start(): Promise<void> {
    let data: GameData;
    try {
      data = await loadGameData();
    } catch (error) {
      this.add
        .text(this.scale.width / 2, this.scale.height / 2, `Assets missing.\nRun: npm run extract\n\n${String(error)}`, {
          fontFamily: "Krungthep",
          fontSize: "24px",
          align: "center",
          wordWrap: { width: this.scale.width - 80 },
        })
        .setOrigin(0.5);
      return;
    }
    this.scene.start("match", { data });
  }
}
