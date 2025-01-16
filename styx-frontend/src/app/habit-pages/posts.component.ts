import { Component, OnInit } from '@angular/core';
import { PostsService } from './posts.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '@auth0/auth0-angular';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-posts',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './posts.component.html',
  styleUrls: ['./posts.component.sass'],
})
export class PostsComponent implements OnInit {
  postType: string | null = null; // e.g., "dogs", "smoking"
  posts: any[] = [];
  error: string | null = null;
  formData = {
    caption: '',
  };
  selectedMediaFile: File | null = null;
  commentText: string = ''; // For top-level comment
  replyText: string = ''; // For reply text
  replyingToCommentId: string | null = null; // Track which comment is being replied to
  commentingOnPostId: string | null = null;
  isLoading: boolean = false; // Flag to track loading state

  constructor(
    private postsService: PostsService,
    public auth: AuthService,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    // 1) Get the postType from the route param
    this.postType = this.route.snapshot.paramMap.get('postType');
    this.fetchPosts(this.postType ?? 'defaultType');

    // (Optional) We could track the logged-in user here, but
    // we no longer pass 'auth0UserId' to the service calls.
    this.auth.user$.subscribe((user) => {
      if (!user) {
        console.log('User not logged in or user info not yet available.');
      }
    });
  }

  /**
   * Fetch posts by postType.
   * The service doesn't need user info for this (unless your API requires a token).
   */
  fetchPosts(postType: string): void {
    this.postsService.getPostsByType(postType).subscribe({
      next: (data) => {
        if (data === null) {
          this.posts = [];
          this.error = 'No posts available for the selected type.';
        } else {
          this.posts = data;
          // Sort posts by descending CreatedAt
          this.posts.sort(
            (a, b) =>
              new Date(b.CreatedAt).getTime() - new Date(a.CreatedAt).getTime()
          );
          this.error = null;
        }
      },
      error: (err) => {
        console.error('Error fetching posts:', err);
        this.error = 'Failed to fetch posts. Please try again later.';
        this.posts = [];
      },
    });
  }

  /**
   * Handle file selection for creating a post with optional media
   */
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedMediaFile = input.files[0];
    } else {
      this.selectedMediaFile = null;
    }
  }

  /**
   * Create a new post (with or without media).
   * The backend identifies the user from the token's sub claim.
   */
  onSubmit(): void {
    const postType = this.postType ?? 'defaultType';
    const name = localStorage.getItem('userName') || 'Anonymous';
    const email = localStorage.getItem('userEmail') || '';

    this.isLoading = true;
    this.postsService
      .createPostWithOptionalMedia(
        postType,
        name,
        email,
        this.formData.caption,
        this.selectedMediaFile || undefined
      )
      .subscribe({
        next: (response) => {
          this.isLoading = false;
          console.log('Post created successfully:', response);

          // If the backend returned a message about coins
          if (response.message?.includes('500 coins rewarded successfully.')) {
            alert(
              `Congratulations! ${response.message} Your total coins: ${response.totalCoins}`
            );
          } else {
            alert('Post created!');
          }

          this.resetForm();
          this.fetchPosts(postType);
        },
        error: (error) => {
          this.isLoading = false;
          console.error('Error creating post:', error);
          alert('Failed to create post.');
        },
      });
  }

  /**
   * Reset the post creation form after submission
   */
  resetForm(): void {
    this.formData.caption = '';
    this.selectedMediaFile = null;
    const fileInput = document.getElementById('mediaFile') as HTMLInputElement;
    if (fileInput) {
      fileInput.value = ''; // Reset file input
    }
  }

  /**
   * Toggle the comment form for a specific post
   */
  toggleCommentForm(postId: string) {
    this.commentingOnPostId =
      this.commentingOnPostId === postId ? null : postId;
  }

  /**
   * Toggle the reply form for a specific comment
   */
  toggleReplyForm(commentId: string) {
    this.replyingToCommentId =
      this.replyingToCommentId === commentId ? null : commentId;
  }

  /**
   * Add a comment or reply
   * The user ID is derived from the token on the backend.
   */
  addComment(postId: string, postEmail: string, commentId?: string) {
    const name = localStorage.getItem('userName') || 'Anonymous';
    const email = localStorage.getItem('userEmail') || '';

    const text = commentId ? this.replyText : this.commentText;
    if (!text.trim()) {
      alert('Comment text is required.');
      return;
    }

    // postsService.addComment no longer needs 'auth0UserId'
    this.postsService
      .addComment(postId, text, email, postEmail, name, commentId)
      .subscribe({
        next: () => {
          alert(
            commentId
              ? 'Reply added successfully!'
              : 'Comment added successfully!'
          );
          this.resetCommentFields();
          this.fetchPosts(this.postType ?? 'defaultType');
        },
        error: (err) => {
          console.error('Error adding comment:', err);
          alert('Failed to add comment/reply.');
        },
      });
  }

  /**
   * Reset comment fields after submission
   */
  resetCommentFields() {
    this.commentText = '';
    this.replyText = '';
    this.commentingOnPostId = null;
    this.replyingToCommentId = null;
  }

  /**
   * Toggle a like for a post, comment, or reply.
   * The user is identified from the token on the backend, no 'auth0UserId'.
   */
  onToggleLike(postId: string, commentId?: string, replyId?: string): void {
    this.postsService.toggleLike(postId, commentId, replyId).subscribe({
      next: (response) => {
        console.log('Like toggled successfully:', response);
        this.fetchPosts(this.postType ?? 'defaultType');
      },
      error: (err) => {
        console.error('Error toggling like:', err);
        alert('Failed to toggle like.');
      },
    });
  }

  /**
   * Delete a post
   * The user is identified from the token's sub.
   */
  deletePost(postId: string): void {
    const confirmDelete = window.confirm(
      'Are you sure you want to delete this post?'
    );
    if (!confirmDelete) return;

    this.postsService.deletePost(postId).subscribe({
      next: () => {
        console.log('Post deleted successfully.');
        this.fetchPosts(this.postType ?? 'defaultType');
      },
      error: (error) => {
        console.error('Error deleting post:', error);
      },
    });
  }

  /**
   * Edit a comment or reply
   */
  editComment(postId: string, commentId: string, replyId?: string): void {
    // Find the post & comment in the local array to prompt the user for new text
    const post = this.posts.find((p) => p.id === postId);
    if (!post) {
      console.error('Post not found.');
      return;
    }

    const comment = post.Comments?.find((c: any) => c.CommentId === commentId);
    if (!comment) {
      console.error('Comment not found.');
      return;
    }

    let currentText = comment.Text;
    if (replyId) {
      const reply = comment.Replies?.find((r: any) => r.CommentId === replyId);
      if (!reply) {
        console.error('Reply not found.');
        return;
      }
      currentText = reply.Text;
    }

    const newText = window.prompt('Edit your comment:', currentText);
    if (!newText || newText === currentText) {
      console.log('Edit canceled or no changes made.');
      return;
    }

    // Now call the service
    this.postsService
      .editComment(newText, postId, commentId, replyId)
      .subscribe({
        next: () => {
          console.log('Comment edited successfully.');
          this.fetchPosts(this.postType ?? 'defaultType');
        },
        error: (err) => {
          console.error('Error editing comment:', err);
        },
      });
  }

  /**
   * Delete a comment or reply
   */
  deleteComment(postId: string, commentId: string, replyId?: string): void {
    this.postsService.deleteComment(postId, commentId, replyId).subscribe({
      next: () => {
        console.log('Comment deleted successfully.');
        this.fetchPosts(this.postType ?? 'defaultType');
      },
      error: (err) => {
        console.error('Error deleting comment:', err);
      },
    });
  }

  /**
   * Confirm delete comment
   */
  confirmDeleteComment(postId: string, commentId: string): void {
    const confirmDelete = window.confirm(
      'Are you sure you want to delete this comment?'
    );
    if (confirmDelete) {
      this.deleteComment(postId, commentId);
    }
  }

  /**
   * Confirm delete reply
   */
  confirmDeleteReply(postId: string, commentId: string, replyId: string): void {
    const confirmDelete = window.confirm(
      'Are you sure you want to delete this reply?'
    );
    if (confirmDelete) {
      this.deleteComment(postId, commentId, replyId);
    }
  }

  /**
   * Check if a URL points to an image
   */
  isImage(url: string): boolean {
    const imageExtensions = ['jpg', 'jpeg', 'png', 'gif', 'webp'];
    const fileExtension = url.split('.').pop()?.toLowerCase();
    return imageExtensions.includes(fileExtension || '');
  }
}
