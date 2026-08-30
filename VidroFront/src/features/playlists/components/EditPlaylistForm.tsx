import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Button } from "#/components/ui/button";
import { Input } from "#/components/ui/input";
import { Label } from "#/components/ui/label";
import {
	Select,
	SelectContent,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "#/components/ui/select";
import { Textarea } from "#/components/ui/textarea";
import { getApiErrorMessage } from "#/shared/lib/error-messages";
import { PlaylistVisibility } from "#/shared/types";
import { useUpdatePlaylist } from "../hooks";
import type { Playlist } from "../types";

const NAME_MAX = 100;
const DESCRIPTION_MAX = 500;

const schema = z.object({
	name: z
		.string()
		.min(1, "Name is required")
		.max(NAME_MAX, `Name must be at most ${NAME_MAX} characters`),
	description: z
		.string()
		.max(
			DESCRIPTION_MAX,
			`Description must be at most ${DESCRIPTION_MAX} characters`,
		)
		.optional(),
	visibility: z.coerce.number(),
});

type FormValues = z.infer<typeof schema>;

type Props = {
	playlist: Playlist;
	onSuccess?: () => void;
};

export function EditPlaylistForm({ playlist, onSuccess }: Props) {
	const updatePlaylist = useUpdatePlaylist(playlist.playlistId);

	const {
		register,
		handleSubmit,
		setValue,
		formState: { errors },
	} = useForm<FormValues>({
		resolver: zodResolver(schema),
		defaultValues: {
			name: playlist.name,
			description: playlist.description ?? "",
			visibility: playlist.visibility.id,
		},
	});

	function onSubmit(values: FormValues) {
		updatePlaylist.mutate(
			{
				name: values.name,
				description: values.description?.trim() || null,
				visibility: values.visibility,
			},
			{ onSuccess },
		);
	}

	return (
		<form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
			<div className="space-y-2">
				<Label htmlFor="edit-playlist-name">Name</Label>
				<Input id="edit-playlist-name" {...register("name")} />
				{errors.name && (
					<p className="text-sm text-destructive">{errors.name.message}</p>
				)}
			</div>

			<div className="space-y-2">
				<Label htmlFor="edit-playlist-description">Description</Label>
				<Textarea
					id="edit-playlist-description"
					{...register("description")}
					rows={3}
					placeholder="What is this playlist about? (optional)"
				/>
				{errors.description && (
					<p className="text-sm text-destructive">
						{errors.description.message}
					</p>
				)}
			</div>

			<div className="space-y-2">
				<Label>Visibility</Label>
				<Select
					defaultValue={String(playlist.visibility.id)}
					onValueChange={(v) => setValue("visibility", Number(v))}
				>
					<SelectTrigger>
						<SelectValue />
					</SelectTrigger>
					<SelectContent>
						<SelectItem value={String(PlaylistVisibility.Public)}>
							Public
						</SelectItem>
						<SelectItem value={String(PlaylistVisibility.Private)}>
							Private
						</SelectItem>
					</SelectContent>
				</Select>
			</div>

			{updatePlaylist.error && (
				<p className="text-sm text-destructive">
					{getApiErrorMessage(updatePlaylist.error)}
				</p>
			)}

			<Button type="submit" disabled={updatePlaylist.isPending}>
				{updatePlaylist.isPending ? "Saving…" : "Save changes"}
			</Button>
		</form>
	);
}
